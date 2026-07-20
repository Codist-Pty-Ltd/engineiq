using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Search;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class CodeChunkRepository : ICodeChunkRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;

    public CodeChunkRepository(IDbContextFactory<EngineIQDbContext> factory)
    {
        _factory = factory;
    }

    public async Task UpsertBatchAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<CodeChunkEmbeddingRow> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var filePaths = chunks.Select(c => c.Candidate.FilePath).Distinct().ToList();
        var hashes = chunks.Select(c => c.Candidate.ContentSha256).Distinct().ToList();

        var existing = await db.CodeChunks
            .Where(c =>
                c.TenantId == tenantId
                && c.RepositoryId == repositoryId
                && filePaths.Contains(c.FilePath)
                && hashes.Contains(c.ContentSha256))
            .ToListAsync(cancellationToken);
        var existingMap = existing.ToDictionary(c => (c.FilePath, c.ContentSha256));

        foreach (var row in chunks)
        {
            var candidate = row.Candidate;
            var key = (candidate.FilePath, candidate.ContentSha256);
            if (existingMap.TryGetValue(key, out var entity))
            {
                entity.ChunkIndex = candidate.ChunkIndex;
                entity.StartLine = candidate.StartLine;
                entity.EndLine = candidate.EndLine;
                entity.SymbolName = candidate.SymbolName;
                entity.Kind = candidate.Kind;
                entity.Content = candidate.Content;
                entity.Embedding = new Vector(row.Embedding);
            }
            else
            {
                db.CodeChunks.Add(new CodeChunk
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RepositoryId = repositoryId,
                    FilePath = candidate.FilePath,
                    ChunkIndex = candidate.ChunkIndex,
                    Content = candidate.Content,
                    StartLine = candidate.StartLine,
                    EndLine = candidate.EndLine,
                    ContentSha256 = candidate.ContentSha256,
                    SymbolName = candidate.SymbolName,
                    Kind = candidate.Kind,
                    Embedding = new Vector(row.Embedding),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent index run race — other run wins; next job retries.
        }
    }

    public async Task<int> DeleteByFilePathsAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
            return 0;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.CodeChunks
            .Where(c => c.TenantId == tenantId && c.RepositoryId == repositoryId && filePaths.Contains(c.FilePath))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteExceptFilePathsAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> keepFilePaths,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        if (keepFilePaths.Count == 0)
        {
            return await db.CodeChunks
                .Where(c => c.TenantId == tenantId && c.RepositoryId == repositoryId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return await db.CodeChunks
            .Where(c =>
                c.TenantId == tenantId
                && c.RepositoryId == repositoryId
                && !keepFilePaths.Contains(c.FilePath))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteStaleHashesAsync(
        Guid tenantId,
        Guid repositoryId,
        string filePath,
        IReadOnlyList<string> keepContentSha256,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.CodeChunks
            .Where(c =>
                c.TenantId == tenantId
                && c.RepositoryId == repositoryId
                && c.FilePath == filePath
                && !keepContentSha256.Contains(c.ContentSha256))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHashesForFilesAsync(
        Guid tenantId,
        Guid repositoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
            return new Dictionary<string, IReadOnlySet<string>>();

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var rows = await db.CodeChunks.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.RepositoryId == repositoryId && filePaths.Contains(c.FilePath))
            .Select(c => new { c.FilePath, c.ContentSha256 })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.FilePath)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.ContentSha256).ToHashSet());
    }

    public async Task<int> CountByRepoAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.CodeChunks.CountAsync(c => c.TenantId == tenantId && c.RepositoryId == repositoryId, cancellationToken);
    }

    public async Task<IReadOnlyList<VectorHit>> VectorSearchAsync(
        Guid tenantId,
        IReadOnlyList<Guid> repositoryIds,
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (repositoryIds.Count == 0 || queryEmbedding.Length == 0 || topK <= 0)
            return Array.Empty<VectorHit>();

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            return Array.Empty<VectorHit>();

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT c.id, c.repository_id, r.full_name, c.file_path, c.symbol_name, c.start_line, c.end_line, c.content,
                   (c.embedding <=> @q) AS distance
            FROM public.code_chunks AS c
            INNER JOIN public.repositories AS r ON r.id = c.repository_id AND r.tenant_id = c.tenant_id
            WHERE c.tenant_id = @tenant
              AND c.repository_id = ANY(@repos)
              AND c.embedding IS NOT NULL
            ORDER BY c.embedding <=> @q
            LIMIT @top_k
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("repos", repositoryIds.ToArray());
        cmd.Parameters.AddWithValue("q", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("top_k", topK);

        var hits = new List<VectorHit>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rank = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            rank++;
            hits.Add(new VectorHit(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetDouble(8),
                rank));
        }

        return hits;
    }

    public async Task<IReadOnlyList<TextHit>> FullTextSearchAsync(
        Guid tenantId,
        IReadOnlyList<Guid> repositoryIds,
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (repositoryIds.Count == 0 || topK <= 0 || string.IsNullOrWhiteSpace(query))
            return Array.Empty<TextHit>();

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            return Array.Empty<TextHit>();

        var identifierTsQuery = CodeSearchQueryTokens.BuildIdentifierTsQuery(query);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        try
        {
            return await ExecuteFullTextAsync(conn, tenantId, repositoryIds, query, identifierTsQuery, topK, cancellationToken);
        }
        catch (PostgresException) when (identifierTsQuery is not null)
        {
            return await ExecuteFullTextAsync(conn, tenantId, repositoryIds, query, null, topK, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<TextHit>> ExecuteFullTextAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        IReadOnlyList<Guid> repositoryIds,
        string query,
        string? identifierTsQuery,
        int topK,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT c.id, c.repository_id, r.full_name, c.file_path, c.symbol_name, c.start_line, c.end_line, c.content,
                   ts_rank(c.content_tsv, q.query) AS rank_score
            FROM public.code_chunks AS c
            INNER JOIN public.repositories AS r ON r.id = c.repository_id AND r.tenant_id = c.tenant_id
            CROSS JOIN LATERAL (
                SELECT
                    CASE
                        WHEN @idents IS NULL OR length(trim(@idents::text)) = 0 THEN plainto_tsquery('simple', @plain)
                        ELSE plainto_tsquery('simple', @plain) || to_tsquery('simple', @idents)
                    END AS query
            ) AS q
            WHERE c.tenant_id = @tenant
              AND c.repository_id = ANY(@repos)
              AND c.content_tsv @@ q.query
            ORDER BY rank_score DESC
            LIMIT @top_k
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("repos", repositoryIds.ToArray());
        cmd.Parameters.AddWithValue("plain", query);
        cmd.Parameters.AddWithValue("idents", (object?)identifierTsQuery ?? DBNull.Value);
        cmd.Parameters.AddWithValue("top_k", topK);

        var hits = new List<TextHit>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rank = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            rank++;
            hits.Add(new TextHit(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetDouble(8),
                rank));
        }

        return hits;
    }
}
