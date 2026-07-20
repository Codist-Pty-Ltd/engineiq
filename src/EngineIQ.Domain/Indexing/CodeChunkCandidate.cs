namespace EngineIQ.Domain.Indexing;

/// <summary>
/// A chunk of code produced by an <see cref="Interfaces.ICodeChunker"/>, in memory only, before embedding.
/// </summary>
public sealed record CodeChunkCandidate(
    string FilePath,
    int ChunkIndex,
    string Content,
    int StartLine,
    int EndLine,
    string ContentSha256,
    string? SymbolName = null,
    string? Kind = null);

/// <summary>A chunk candidate paired with its embedding vector, ready to persist.</summary>
public sealed record CodeChunkEmbeddingRow(CodeChunkCandidate Candidate, float[] Embedding);
