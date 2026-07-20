using EngineIQ.Domain.Indexing;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Splits file content into indexable chunks. Content is processed in memory only.</summary>
public interface ICodeChunker
{
    Task<IReadOnlyList<CodeChunkCandidate>> ChunkAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default);
}
