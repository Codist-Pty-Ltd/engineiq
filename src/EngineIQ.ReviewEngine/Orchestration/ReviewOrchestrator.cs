using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.ReviewEngine.Orchestration;

/// <summary>
/// In-memory PR review: diff → Claude → GitHub comment (no persistence of source).
/// </summary>
public sealed class ReviewOrchestrator : IReviewOrchestrator
{
    private readonly IGitHubClient _gitHubClient;
    private readonly IAIEngine _aiEngine;

    public ReviewOrchestrator(IGitHubClient gitHubClient, IAIEngine aiEngine)
    {
        _gitHubClient = gitHubClient;
        _aiEngine = aiEngine;
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

        var outcome = await _aiEngine.ReviewDiffAsync(
            diff,
            command.Preferences,
            command.StandardsConfigYaml,
            cancellationToken);

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
