using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Trust;
using EngineIQ.FeedbackGenerator;
using EngineIQ.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ReviewEngine.Orchestration;

/// <summary>
/// In-memory Jira issue analysis: fetch issue → Claude improvement → comment (no issue body persisted).
/// </summary>
public sealed class IssueAnalysisOrchestrator : IIssueAnalysisOrchestrator
{
    private readonly IJiraClient _jiraClient;
    private readonly IJiraIssueImprovementService _improvement;
    private readonly TrustOptions _trust;
    private readonly ILogger<IssueAnalysisOrchestrator> _logger;

    public IssueAnalysisOrchestrator(
        IJiraClient jiraClient,
        IJiraIssueImprovementService improvement,
        IOptions<TrustOptions> trust,
        ILogger<IssueAnalysisOrchestrator> logger)
    {
        _jiraClient = jiraClient;
        _improvement = improvement;
        _trust = trust.Value;
        _logger = logger;
    }

    public async Task<IssueAnalysisOutcome> AnalyzeIssueAsync(
        JiraIssueAnalysisJobCommand command,
        CancellationToken cancellationToken = default)
    {
        JiraIssueDetails? issue;
        using (ReviewTelemetry.StartActivity("jira.issue.fetch"))
        {
            issue = await _jiraClient.GetIssueAsync(command.Connection, command.IssueKey, cancellationToken);
        }

        if (issue is null)
            throw new IssueNotFoundException(command.IssueKey);

        IssueImprovementResult result;
        int inputTokens;
        int outputTokens;
        decimal costZar;
        using (ReviewTelemetry.StartActivity("jira.issue.claude"))
        {
            (result, inputTokens, outputTokens, costZar) =
                await _improvement.ImproveAsync(issue, cancellationToken);
        }

        var footer = BuildIssueTrustFooter(_trust.PublicApiBaseUrl);
        var comment = IssueAnalysisCommentFormatter.Format(result, footer);

        using (ReviewTelemetry.StartActivity("jira.issue.comment.post"))
        {
            await _jiraClient.PostCommentAsync(command.Connection, command.IssueKey, comment, cancellationToken);
        }

        _logger.LogInformation(
            "Jira issue analysis posted for {IssueKey} (tenant {TenantId}).",
            command.IssueKey,
            command.TenantId);

        return new IssueAnalysisOutcome(inputTokens, outputTokens, costZar);
    }

    private static string BuildIssueTrustFooter(string publicApiBaseUrl)
    {
        var baseUrl = (publicApiBaseUrl ?? "https://api.engineiq.co.za").TrimEnd('/');
        return $"""


----
EngineIQ processed this issue ephemerally. No issue content was stored. Findings metadata only is retained for your dashboard. [View our security model|{baseUrl}/security]
""";
    }
}
