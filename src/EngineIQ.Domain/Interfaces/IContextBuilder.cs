using EngineIQ.Domain.Context;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Builds or retrieves cached per-repo architecture context (Redis-backed).</summary>
public interface IContextBuilder
{
    /// <summary>
    /// Returns cached context when valid; rebuilds on miss or structural PR changes.
    /// Returns null when context cannot be built (caller continues diff-only review).
    /// </summary>
    Task<RepoContext?> GetOrBuildAsync(
        Guid tenantId,
        long installationId,
        string owner,
        string repo,
        IReadOnlyList<string> prFilePaths,
        CancellationToken cancellationToken = default);
}
