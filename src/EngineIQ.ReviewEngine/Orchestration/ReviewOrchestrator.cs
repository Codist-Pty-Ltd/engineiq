using EngineIQ.AIEngine.Anthropic;
using EngineIQ.ContextBuilder.Parsing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;
using EngineIQ.Domain.Trust;
using EngineIQ.FeedbackGenerator;
using EngineIQ.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ReviewEngine.Orchestration;

/// <summary>
/// In-memory PR review: diff → repo context → standards rules → Claude → merged comment (no source persisted).
/// </summary>
public sealed class ReviewOrchestrator : IReviewOrchestrator
{
    private readonly IGitHubClient _gitHubClient;
    private readonly IContextBuilder _contextBuilder;
    private readonly IStandardsEngine _standardsEngine;
    private readonly IAIEngine _aiEngine;
    private readonly TrustOptions _trust;
    private readonly ILogger<ReviewOrchestrator> _logger;

    public ReviewOrchestrator(
        IGitHubClient gitHubClient,
        IContextBuilder contextBuilder,
        IStandardsEngine standardsEngine,
        IAIEngine aiEngine,
        IOptions<TrustOptions> trust,
        ILogger<ReviewOrchestrator> logger)
    {
        _gitHubClient = gitHubClient;
        _contextBuilder = contextBuilder;
        _standardsEngine = standardsEngine;
        _aiEngine = aiEngine;
        _trust = trust.Value;
        _logger = logger;
    }

    public async Task<PrReviewJobResult> ReviewPullRequestAsync(
        PrReviewJobCommand command,
        CancellationToken cancellationToken = default)
    {
        var pr = await _gitHubClient.GetPullRequestInfoAsync(
            command.InstallationId,
            command.Owner,
            command.Repo,
            command.PrNumber,
            cancellationToken);

        if (!ReviewEnqueuePolicy.ShouldEnqueue(command.Preferences, pr.IsDraft, out var skipReason))
            return new PrReviewJobResult(true, skipReason, null);

        var diff = await _gitHubClient.GetPullRequestDiffAsync(
            command.InstallationId,
            command.Owner,
            command.Repo,
            command.PrNumber,
            cancellationToken);

        var footer = AnthropicReviewResponseParser.BuildTrustFooter(_trust.PublicApiBaseUrl);
        if (string.IsNullOrWhiteSpace(diff))
        {
            var emptyOutcome = new PrReviewDiffOutcome(
                "_No changes to review._" + footer,
                0,
                0,
                0m,
                0,
                Array.Empty<FindingWriteDto>());
            await _gitHubClient.PostReviewCommentAsync(
                command.InstallationId,
                command.Owner,
                command.Repo,
                command.PrNumber,
                emptyOutcome.CommentBody,
                cancellationToken);
            return new PrReviewJobResult(false, null, emptyOutcome);
        }

        var prFilePaths = DiffPathExtractor.ExtractFilePaths(diff);
        Domain.Context.RepoContext? repoContext;
        using (ReviewTelemetry.StartActivity("review.context"))
        {
            repoContext = await TryGetRepoContextAsync(command, prFilePaths, cancellationToken);
        }

        IReadOnlyList<FindingWriteDto> ruleFindings;
        using (ReviewTelemetry.StartActivity("review.standards"))
        {
            ruleFindings = _standardsEngine.EvaluateDiff(diff, command.StandardsConfigYaml, repoContext);
        }

        PrReviewDiffOutcome aiOutcome;
        using (ReviewTelemetry.StartActivity("review.claude"))
        {
            aiOutcome = await _aiEngine.ReviewDiffAsync(
                diff,
                command.Preferences,
                command.StandardsConfigYaml,
                repoContext,
                cancellationToken);
        }

        var mergedFindings = ReviewFindingsMerger.Merge(ruleFindings, aiOutcome.ParsedFindings);
        var aiNarrative = AnthropicReviewResponseParser.StripTrustFooter(aiOutcome.CommentBody);
        var commentBody = ReviewCommentFormatter.Format(mergedFindings, footer, aiNarrative);

        var outcome = new PrReviewDiffOutcome(
            commentBody,
            aiOutcome.InputTokens,
            aiOutcome.OutputTokens,
            aiOutcome.EstimatedCostZar,
            mergedFindings.Count,
            mergedFindings);

        using (ReviewTelemetry.StartActivity("review.comment.post"))
        {
            await _gitHubClient.PostReviewCommentAsync(
                command.InstallationId,
                command.Owner,
                command.Repo,
                command.PrNumber,
                outcome.CommentBody,
                cancellationToken);
        }

        return new PrReviewJobResult(false, null, outcome);
    }

    private async Task<Domain.Context.RepoContext?> TryGetRepoContextAsync(
        PrReviewJobCommand command,
        IReadOnlyList<string> prFilePaths,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contextBuilder.GetOrBuildAsync(
                command.TenantId,
                command.InstallationId,
                command.Owner,
                command.Repo,
                prFilePaths,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Repo context unavailable for {Owner}/{Repo}; continuing diff-only review",
                command.Owner,
                command.Repo);
            return null;
        }
    }
}
