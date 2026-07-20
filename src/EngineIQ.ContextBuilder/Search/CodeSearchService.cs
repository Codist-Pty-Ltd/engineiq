using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ContextBuilder.Search;

/// <summary>
/// Hybrid retrieval: Voyage query embedding + pgvector cosine + full-text, merged with Reciprocal Rank Fusion.
/// </summary>
public sealed class CodeSearchService : ICodeSearchService
{
    private readonly IEmbeddingClient _embeddings;
    private readonly ICodeChunkRepository _chunks;
    private readonly RetrievalOptions _options;
    private readonly ILogger<CodeSearchService> _logger;

    public CodeSearchService(
        IEmbeddingClient embeddings,
        ICodeChunkRepository chunks,
        IOptions<RetrievalOptions> options,
        ILogger<CodeSearchService> logger)
    {
        _embeddings = embeddings;
        _chunks = chunks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CodeSearchResult> SearchAsync(
        Guid tenantId,
        IReadOnlyList<Guid> repositoryIds,
        string queryText,
        CancellationToken cancellationToken = default)
    {
        if (repositoryIds.Count == 0 || string.IsNullOrWhiteSpace(queryText))
            return CodeSearchResult.Empty;

        float[]? queryEmbedding = null;
        try
        {
            var vectors = await _embeddings.EmbedAsync(
                new[] { queryText },
                EmbeddingInputType.Query,
                cancellationToken);
            queryEmbedding = vectors.Count > 0 ? vectors[0] : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query embedding failed for tenant {TenantId}; continuing with full-text only.", tenantId);
        }

        IReadOnlyList<VectorHit> vectorHits = Array.Empty<VectorHit>();
        IReadOnlyList<TextHit> textHits = Array.Empty<TextHit>();

        var vectorTask = queryEmbedding is null
            ? Task.FromResult(vectorHits)
            : _chunks.VectorSearchAsync(tenantId, repositoryIds, queryEmbedding, _options.VectorTopK, cancellationToken);
        var textTask = _chunks.FullTextSearchAsync(tenantId, repositoryIds, queryText, _options.TextTopK, cancellationToken);

        try
        {
            await Task.WhenAll(vectorTask, textTask);
            vectorHits = await vectorTask;
            textHits = await textTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Code search queries failed for tenant {TenantId}.", tenantId);
            return CodeSearchResult.Empty;
        }

        var merged = MergeRrf(vectorHits, textHits, _options.RrfK);
        var totalCandidates = merged.Count;
        var capped = ApplyCaps(merged, _options);
        return new CodeSearchResult(capped, totalCandidates);
    }

    /// <summary>RRF: score = Σ 1/(k + rank) over lists the chunk appears in.</summary>
    public static List<CodeSearchHit> MergeRrf(
        IReadOnlyList<VectorHit> vectorHits,
        IReadOnlyList<TextHit> textHits,
        int rrfK = 60)
    {
        var scores = new Dictionary<Guid, (double Score, CodeSearchHit Hit)>();

        void Consider(Guid chunkId, int rank, Func<CodeSearchHit> factory)
        {
            var add = 1.0 / (rrfK + rank);
            if (scores.TryGetValue(chunkId, out var existing))
                scores[chunkId] = (existing.Score + add, existing.Hit with { Score = existing.Score + add });
            else
            {
                var hit = factory() with { Score = add };
                scores[chunkId] = (add, hit);
            }
        }

        foreach (var v in vectorHits)
        {
            Consider(
                v.ChunkId,
                v.Rank,
                () => new CodeSearchHit(
                    v.RepositoryId, v.RepositoryName, v.FilePath, v.Symbol, v.StartLine, v.EndLine, v.Content, 0));
        }

        foreach (var t in textHits)
        {
            Consider(
                t.ChunkId,
                t.Rank,
                () => new CodeSearchHit(
                    t.RepositoryId, t.RepositoryName, t.FilePath, t.Symbol, t.StartLine, t.EndLine, t.Content, 0));
        }

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Select(x => x.Hit)
            .ToList();
    }

    /// <summary>MaxHits, per-file cap, MaxContextChars (always keep top 3).</summary>
    public static IReadOnlyList<CodeSearchHit> ApplyCaps(IReadOnlyList<CodeSearchHit> ranked, RetrievalOptions options)
    {
        var maxHits = Math.Max(1, options.MaxHits);
        var maxPerFile = Math.Max(1, options.MaxHitsPerFile);
        var maxChars = Math.Max(1, options.MaxContextChars);
        const int minKeep = 3;

        var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<CodeSearchHit>();
        var totalChars = 0;

        foreach (var hit in ranked)
        {
            if (selected.Count >= maxHits)
                break;

            var fileKey = $"{hit.RepositoryId:D}:{hit.FilePath}";
            perFile.TryGetValue(fileKey, out var count);
            if (count >= maxPerFile)
                continue;

            var nextChars = totalChars + hit.Content.Length;
            if (selected.Count >= minKeep && nextChars > maxChars)
                continue;

            selected.Add(hit);
            perFile[fileKey] = count + 1;
            totalChars = nextChars;
        }

        return selected;
    }
}
