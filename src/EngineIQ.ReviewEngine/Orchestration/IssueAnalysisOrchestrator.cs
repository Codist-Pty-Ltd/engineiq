using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;
using EngineIQ.Domain.Trust;
using EngineIQ.FeedbackGenerator;
using EngineIQ.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ReviewEngine.Orchestration;

/// <summary>
/// In-memory Jira issue analysis: fetch issue → hybrid code search → Claude improvement → comment.
/// </summary>
public sealed class IssueAnalysisOrchestrator : IIssueAnalysisOrchestrator
{
    private const int QueryDescriptionMaxChars = 4000;

    private readonly IJiraClient _jiraClient;
    private readonly IJiraIssueImprovementService _improvement;
    private readonly IJiraProjectRepoMappingRepository _mappings;
    private readonly IRepositoryRepository _repositories;
    private readonly ICodeSearchService _codeSearch;
    private readonly IContextBuilder _contextBuilder;
    private readonly TrustOptions _trust;
    private readonly ILogger<IssueAnalysisOrchestrator> _logger;

    public IssueAnalysisOrchestrator(
        IJiraClient jiraClient,
        IJiraIssueImprovementService improvement,
        IJiraProjectRepoMappingRepository mappings,
        IRepositoryRepository repositories,
        ICodeSearchService codeSearch,
        IContextBuilder contextBuilder,
        IOptions<TrustOptions> trust,
        ILogger<IssueAnalysisOrchestrator> logger)
    {
        _jiraClient = jiraClient;
        _improvement = improvement;
        _mappings = mappings;
        _repositories = repositories;
        _codeSearch = codeSearch;
        _contextBuilder = contextBuilder;
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

        var targetRepos = await ResolveTargetReposAsync(command, issue.ProjectKey, cancellationToken);
        var reposSearched = targetRepos.Count;
        var codeContext = CodeSearchResult.Empty;

        if (targetRepos.Count > 0)
        {
            var queryText = BuildSearchQuery(issue);
            using (ReviewTelemetry.StartActivity("jira.issue.code_search"))
            {
                try
                {
                    codeContext = await _codeSearch.SearchAsync(
                        command.TenantId,
                        targetRepos.Select(r => r.Id).ToList(),
                        queryText,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Code search failed for {IssueKey}; continuing with Slice 1 improvement only.",
                        command.IssueKey);
                    codeContext = CodeSearchResult.Empty;
                }
            }
        }

        Domain.Context.RepoContext? repoContext = null;
        if (!codeContext.IsEmpty)
        {
            var topHitRepoId = codeContext.Hits[0].RepositoryId;
            var topRepo = targetRepos.FirstOrDefault(r => r.Id == topHitRepoId)
                          ?? targetRepos[0];
            repoContext = await TryGetRepoContextAsync(command.TenantId, topRepo, codeContext, cancellationToken);
        }

        IssueImprovementResult result;
        int inputTokens;
        int outputTokens;
        decimal costZar;
        using (ReviewTelemetry.StartActivity("jira.issue.claude"))
        {
            (result, inputTokens, outputTokens, costZar) =
                await _improvement.ImproveAsync(issue, codeContext, repoContext, cancellationToken);
        }

        var footer = BuildIssueTrustFooter(_trust.PublicApiBaseUrl);
        var comment = IssueAnalysisCommentFormatter.Format(result, footer);

        using (ReviewTelemetry.StartActivity("jira.issue.comment.post"))
        {
            await _jiraClient.PostCommentAsync(command.Connection, command.IssueKey, comment, cancellationToken);
        }

        _logger.LogInformation(
            "Jira issue analysis posted for {IssueKey} (tenant {TenantId}, repos={Repos}, chunks={Chunks}).",
            command.IssueKey,
            command.TenantId,
            reposSearched,
            codeContext.Hits.Count);

        return new IssueAnalysisOutcome(
            inputTokens,
            outputTokens,
            costZar,
            reposSearched,
            codeContext.Hits.Count);
    }

    public static string BuildSearchQuery(JiraIssueDetails issue)
    {
        var description = issue.Description ?? string.Empty;
        if (description.Length > QueryDescriptionMaxChars)
            description = description[..QueryDescriptionMaxChars];
        return $"{issue.Summary}\n\n{description}";
    }

    public async Task<IReadOnlyList<RepositoryLookupRow>> ResolveTargetReposAsync(
        JiraIssueAnalysisJobCommand command,
        string projectKey,
        CancellationToken cancellationToken)
    {
        var mappedIds = await _mappings.GetRepositoryIdsForProjectAsync(
            command.TenantId,
            command.JiraConnectionId,
            projectKey,
            cancellationToken);

        var indexed = await _repositories.ListIndexedAsync(command.TenantId, cancellationToken);
        if (mappedIds.Count > 0)
        {
            var mappedSet = mappedIds.ToHashSet();
            return indexed.Where(r => mappedSet.Contains(r.Id)).ToList();
        }

        return indexed;
    }

    private async Task<Domain.Context.RepoContext?> TryGetRepoContextAsync(
        Guid tenantId,
        RepositoryLookupRow repo,
        CodeSearchResult codeContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var (owner, name) = ParseOwnerRepo(repo.FullName);
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name))
                return null;

            var samplePaths = codeContext.Hits
                .Where(h => h.RepositoryId == repo.Id)
                .Select(h => h.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList();

            return await _contextBuilder.GetOrBuildAsync(
                tenantId,
                repo.InstallationId,
                owner,
                name,
                samplePaths,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repo context unavailable for {FullName}; continuing without it.", repo.FullName);
            return null;
        }
    }

    private static (string Owner, string Repo) ParseOwnerRepo(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (string.Empty, string.Empty);
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
