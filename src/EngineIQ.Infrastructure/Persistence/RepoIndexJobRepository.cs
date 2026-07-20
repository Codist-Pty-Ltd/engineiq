using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class RepoIndexJobRepository : IRepoIndexJobRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IOptions<PostgresOptions> _postgres;

    public RepoIndexJobRepository(
        IDbContextFactory<EngineIQDbContext> factory,
        IOptions<PostgresOptions> postgres)
    {
        _factory = factory;
        _postgres = postgres;
    }

    public async Task<RepoIndexJobEnqueueResult> TryCreateQueuedJobAsync(
        Guid tenantId,
        Guid repositoryId,
        long installationId,
        string owner,
        string repo,
        string headSha,
        string? baseSha,
        string dedupeKey,
        CancellationToken cancellationToken = default)
    {
        await using (var gate = await _factory.CreateDbContextAsync(cancellationToken))
        {
            var tenantStatus = await gate.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.Equals(tenantStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                return new RepoIndexJobEnqueueResult(false, tenantId, null, "suspended");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        try
        {
            var job = new RepoIndexJob
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RepositoryId = repositoryId,
                InstallationId = installationId,
                Owner = owner,
                Repo = repo,
                HeadSha = headSha,
                BaseSha = baseSha,
                DedupeKey = dedupeKey,
                Status = ReviewJobStatuses.PendingPublish,
                Attempt = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.RepoIndexJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new RepoIndexJobEnqueueResult(true, tenantId, job.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            var duplicate = await TryResolveDuplicateAsync(tenantId, dedupeKey, cancellationToken);
            return duplicate ?? new RepoIndexJobEnqueueResult(false, tenantId, null, "enqueue_failed");
        }
    }

    private async Task<RepoIndexJobEnqueueResult?> TryResolveDuplicateAsync(
        Guid tenantId,
        string dedupeKey,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var existing = await db.RepoIndexJobs.AsNoTracking()
            .Where(j => j.DedupeKey == dedupeKey)
            .Select(j => new { j.Id, j.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
            return null;

        if (string.Equals(existing.Status, ReviewJobStatuses.PendingPublish, StringComparison.Ordinal))
            return new RepoIndexJobEnqueueResult(false, tenantId, existing.Id, NeedsRepublish: true);

        return new RepoIndexJobEnqueueResult(false, tenantId, existing.Id, "duplicate");
    }

    public async Task<Guid?> FindActiveJobIdForRepoAsync(
        Guid tenantId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.RepoIndexJobs.AsNoTracking()
            .Where(j =>
                j.TenantId == tenantId
                && j.RepositoryId == repositoryId
                && (j.Status == ReviewJobStatuses.PendingPublish
                    || j.Status == ReviewJobStatuses.Queued
                    || j.Status == ReviewJobStatuses.Processing))
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryMarkJobProcessingIfQueuedAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var tenantSuspended = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.Equals(tenantSuspended, "Suspended", StringComparison.OrdinalIgnoreCase))
            return false;

        var updated = await db.RepoIndexJobs
            .Where(j =>
                j.TenantId == tenantId
                && j.Id == jobId
                && (j.Status == ReviewJobStatuses.Queued || j.Status == ReviewJobStatuses.PendingPublish))
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, ReviewJobStatuses.Processing),
                cancellationToken);
        return updated > 0;
    }

    public async Task<bool> MarkJobQueuedAfterPublishAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var updated = await db.RepoIndexJobs
            .Where(j => j.TenantId == tenantId && j.Id == jobId && j.Status == ReviewJobStatuses.PendingPublish)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, ReviewJobStatuses.Queued),
                cancellationToken);
        return updated > 0;
    }

    public async Task<IReadOnlyList<PendingRepoIndexPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var cutoff = DateTimeOffset.UtcNow - staleOlderThan;
        var cs = _postgres.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            return Array.Empty<PendingRepoIndexPublishJobInfo>();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tenant_id, job_id, repository_id, installation_id, owner, repo, head_sha, base_sha
            FROM public.fn_list_stale_pending_repo_index_jobs(@cutoff, @lim)
            """,
            conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        cmd.Parameters.AddWithValue("lim", limit);

        var results = new List<PendingRepoIndexPublishJobInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PendingRepoIndexPublishJobInfo(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    public async Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int filesWalked,
        int chunksTotal,
        int chunksEmbedded,
        int chunksDeleted,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.RepoIndexJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        job.FilesWalked = filesWalked;
        job.ChunksTotal = chunksTotal;
        job.ChunksEmbedded = chunksEmbedded;
        job.ChunksDeleted = chunksDeleted;
        job.FailureReason = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkJobFailedAsync(
        Guid tenantId,
        Guid jobId,
        string? failureReason,
        long? durationMs,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.RepoIndexJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        job.FailureReason = failureReason;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            return true;
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
