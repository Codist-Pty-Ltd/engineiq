using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;
using EngineIQ.Domain.Trust;
using EngineIQ.FeedbackGenerator;
using Microsoft.Extensions.Options;

namespace EngineIQ.ReviewEngine.Orchestration;

/// <summary>
/// In-memory PR review: diff → standards rules → Claude → merged comment (no source persisted).
/// </summary>
public sealed class ReviewOrchestrator : IReviewOrchestrator
{
    private readonly IGitHubClient _gitHubClient;
    private readonly IStandardsEngine _standardsEngine;
    private readonly IAIEngine _aiEngine;
    private readonly TrustOptions _trust;

    public ReviewOrchestrator(
        IGitHubClient gitHubClient,
        IStandardsEngine standardsEngine,
        IAIEngine aiEngine,
        IOptions<TrustOptions> trust)
    {
        _gitHubClient = gitHubClient;
        _standardsEngine = standardsEngine;
        _aiEngine = aiEngine;
        _trust = trust.Value;
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

        var ruleFindings = _standardsEngine.EvaluateDiff(diff, command.StandardsConfigYaml);
        var aiOutcome = await _aiEngine.ReviewDiffAsync(
            diff,
            command.Preferences,
            command.StandardsConfigYaml,
            cancellationToken);

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

        await _gitHubClient.PostReviewCommentAsync(
            command.InstallationId,
            command.Owner,
            command.Repo,
            command.PrNumber,
            outcome.CommentBody,
            cancellationToken);

        return new PrReviewJobResult(false, null, outcome);
    }
}
