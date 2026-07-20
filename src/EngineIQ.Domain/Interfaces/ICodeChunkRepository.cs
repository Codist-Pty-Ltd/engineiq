using EngineIQ.Domain.Indexing;

namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Persisted code chunks + embeddings for semantic search (<c>code_chunks</c>). All operations require
/// explicit tenant scope; only non-sensitive metadata and embeddings are stored — never raw diffs.
/// </summary>
public interface ICodeChunkRepository
{
    /// <summary>Inserts new (repository, file_path, content_sha256) rows and updates existing matches in place.</summary>
    Task UpsertBatchAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<CodeChunkEmbeddingRow> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Removes all chunks for files that no longer exist in the repository. Returns the number removed.</summary>
    Task<int> DeleteByFilePathsAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>Removes all chunks for the repository whose file path is not in <paramref name="keepFilePaths"/>. Returns the number removed.</summary>
    Task<int> DeleteExceptFilePathsAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> keepFilePaths,
        CancellationToken cancellationToken = default);

    /// <summary>Removes chunks for <paramref name="filePath"/> whose hash is not in <paramref name="keepContentSha256"/>. Returns the number removed.</summary>
    Task<int> DeleteStaleHashesAsync(
        Guid tenantId,
        Guid repositoryId,
        string filePath,
        IReadOnlyList<string> keepContentSha256,
        CancellationToken cancellationToken = default);

    /// <summary>Existing content hashes per file path, so the indexer can skip re-embedding unchanged chunks.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHashesForFilesAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);

    Task<int> CountByRepoAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default);
}
