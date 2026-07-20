namespace EngineIQ.API.Indexing;

/// <summary>Pure push-webhook gate for repo indexing (unit-tested without the full controller).</summary>
public static class RepoIndexPushFilter
{
    public static bool IsDefaultBranchPush(string? gitRef, string? defaultBranch)
    {
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch;
        return string.Equals(gitRef, $"refs/heads/{branch}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Incremental push indexing requires a prior full index. Returns false (skip) when never indexed.
    /// </summary>
    public static bool CanEnqueueIncremental(string? indexedCommitSha) =>
        !string.IsNullOrWhiteSpace(indexedCommitSha);

    public static string BuildPushDedupeKey(Guid repositoryId, string afterSha) =>
        $"{repositoryId:D}:{afterSha}";
}
