using EngineIQ.Domain.Indexing;

namespace EngineIQ.Domain.Interfaces;

/// <summary>GitHub App operations for repository code indexing: tarball snapshots and commit comparisons.</summary>
public interface IRepoArchiveClient
{
    /// <summary>Downloads a tarball snapshot of the repository at <paramref name="refOrSha"/> (in memory/temp only).</summary>
    Task<Stream> DownloadTarballAsync(
        long installationId,
        string owner,
        string repo,
        string refOrSha,
        CancellationToken cancellationToken = default);

    /// <summary>Changed files between two commits. <see cref="CompareResult.Truncated"/> signals a fallback to full re-index.</summary>
    Task<CompareResult> CompareAsync(
        long installationId,
        string owner,
        string repo,
        string baseSha,
        string headSha,
        CancellationToken cancellationToken = default);

    /// <summary>HEAD commit sha of the repository's default branch (manual index trigger).</summary>
    Task<string> GetDefaultBranchHeadShaAsync(
        long installationId,
        string owner,
        string repo,
        CancellationToken cancellationToken = default);
}
