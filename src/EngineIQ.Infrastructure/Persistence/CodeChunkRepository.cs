using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
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
            // Concurrent index run for the same repo raced this upsert (rare — the job repository
            // rejects concurrent triggers); the other run's write wins and this batch is retried next job.
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
}
