namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// GitHub App operations: fetch PR diff, post review comment.
/// </summary>
public interface IGitHubClient
{
    /// <summary>
    /// Gets the unified diff for a pull request (in memory only).
    /// </summary>
    Task<string> GetPullRequestDiffAsync(long installationId, string owner, string repo, int prNumber, CancellationToken cancellationToken = default);

    /// <summary>PR metadata for gating (draft detection).</summary>
    Task<GitHubPullRequestInfo> GetPullRequestInfoAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a review comment to the pull request.
    /// </summary>
    Task PostReviewCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>All file paths in the default branch (for architecture context indexing).</summary>
    Task<IReadOnlyList<string>> GetRepositoryFilePathsAsync(
        long installationId,
        string owner,
        string repo,
        CancellationToken cancellationToken = default);
}
