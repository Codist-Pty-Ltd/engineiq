namespace EngineIQ.Domain.Search;

/// <summary>A single hybrid-search hit from the code index (in memory only for the Claude call).</summary>
public sealed record CodeSearchHit(
    Guid RepositoryId,
    string RepositoryName,
    string FilePath,
    string? Symbol,
    int StartLine,
    int EndLine,
    string Content,
    double Score);

/// <summary>Merged retrieval result for issue impact analysis.</summary>
public sealed record CodeSearchResult(IReadOnlyList<CodeSearchHit> Hits, int TotalCandidates)
{
    public static CodeSearchResult Empty { get; } = new(Array.Empty<CodeSearchHit>(), 0);

    public bool IsEmpty => Hits.Count == 0;
}

/// <summary>Raw vector-neighbour row before RRF merge.</summary>
public sealed record VectorHit(
    Guid ChunkId,
    Guid RepositoryId,
    string RepositoryName,
    string FilePath,
    string? Symbol,
    int StartLine,
    int EndLine,
    string Content,
    double Distance,
    int Rank);

/// <summary>Raw full-text hit before RRF merge.</summary>
public sealed record TextHit(
    Guid ChunkId,
    Guid RepositoryId,
    string RepositoryName,
    string FilePath,
    string? Symbol,
    int StartLine,
    int EndLine,
    string Content,
    double RankScore,
    int Rank);
