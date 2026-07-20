using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class BacklogBackfillRepository : IBacklogBackfillRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IOptions<PostgresOptions> _postgres;

    public BacklogBackfillRepository(
        IDbContextFactory<EngineIQDbContext> factory,
        IOptions<PostgresOptions> postgres)
    {
        _factory = factory;
        _postgres = postgres;
    }

    public async Task<BacklogBackfillEnqueueResult> TryCreateQueuedAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string jql,
        int maxIssues,
        CancellationToken cancellationToken = default)
    {
        await using (var gate = await _factory.CreateDbContextAsync(cancellationToken))
        {
            var tenantStatus = await gate.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.Equals(tenantStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                return new BacklogBackfillEnqueueResult(false, tenantId, null, "suspended");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var activeId = await db.BacklogBackfills.AsNoTracking()
            .Where(j =>
                j.TenantId == tenantId
                && j.JiraConnectionId == jiraConnectionId
                && (j.Status == ReviewJobStatuses.PendingPublish
                    || j.Status == ReviewJobStatuses.Queued
                    || j.Status == ReviewJobStatuses.Processing))
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeId is not null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new BacklogBackfillEnqueueResult(false, tenantId, activeId, "in_progress");
        }

        var job = new BacklogBackfill
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            JiraConnectionId = jiraConnectionId,
            Jql = jql,
            Status = ReviewJobStatuses.PendingPublish,
            StartAtCursor = 0,
            MatchedTotal = 0,
            EnqueuedCount = 0,
            SkippedCount = 0,
            MaxIssues = maxIssues,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BacklogBackfills.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new BacklogBackfillEnqueueResult(true, tenantId, job.Id);
    }

    public async Task<Guid?> FindActiveJobIdAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.BacklogBackfills.AsNoTracking()
            .Where(j =>
                j.TenantId == tenantId
                && j.JiraConnectionId == jiraConnectionId
                && (j.Status == ReviewJobStatuses.PendingPublish
                    || j.Status == ReviewJobStatuses.Queued
                    || j.Status == ReviewJobStatuses.Processing))
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<BacklogBackfillRow?> GetByIdAsync(
        Guid tenantId,
        Guid backfillId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        return await db.BacklogBackfills.AsNoTracking()
            .Where(j => j.TenantId == tenantId && j.Id == backfillId)
            .Select(j => new BacklogBackfillRow(
                j.Id,
                j.TenantId,
                j.JiraConnectionId,
                j.Jql,
                j.Status,
                j.StartAtCursor,
                j.MatchedTotal,
                j.EnqueuedCount,
                j.SkippedCount,
                j.MaxIssues,
                j.CreatedAt,
                j.CompletedAt,
                j.FailureReason))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryMarkProcessingIfQueuedAsync(
        Guid tenantId,
        Guid backfillId,
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

        var updated = await db.BacklogBackfills
            .Where(j =>
                j.TenantId == tenantId
                && j.Id == backfillId
                && (j.Status == ReviewJobStatuses.Queued || j.Status == ReviewJobStatuses.PendingPublish))
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, ReviewJobStatuses.Processing),
                cancellationToken);
        return updated > 0;
    }

    public async Task<bool> MarkQueuedAfterPublishAsync(
        Guid tenantId,
        Guid backfillId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var updated = await db.BacklogBackfills
            .Where(j => j.TenantId == tenantId && j.Id == backfillId && j.Status == ReviewJobStatuses.PendingPublish)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, ReviewJobStatuses.Queued),
                cancellationToken);
        return updated > 0;
    }

    public async Task UpdateProgressAsync(
        Guid tenantId,
        Guid backfillId,
        int startAtCursor,
        int matchedTotal,
        int enqueuedCount,
        int skippedCount,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        await db.BacklogBackfills
            .Where(j => j.TenantId == tenantId && j.Id == backfillId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.StartAtCursor, startAtCursor)
                    .SetProperty(j => j.MatchedTotal, matchedTotal)
                    .SetProperty(j => j.EnqueuedCount, enqueuedCount)
                    .SetProperty(j => j.SkippedCount, skippedCount),
                cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid tenantId,
        Guid backfillId,
        int matchedTotal,
        int enqueuedCount,
        int skippedCount,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.BacklogBackfills.FirstOrDefaultAsync(j => j.Id == backfillId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.MatchedTotal = matchedTotal;
        job.EnqueuedCount = enqueuedCount;
        job.SkippedCount = skippedCount;
        job.FailureReason = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid tenantId,
        Guid backfillId,
        string? failureReason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.BacklogBackfills.FirstOrDefaultAsync(j => j.Id == backfillId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.FailureReason = failureReason;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingBackfillPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var cutoff = DateTimeOffset.UtcNow - staleOlderThan;
        var cs = _postgres.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            return Array.Empty<PendingBackfillPublishJobInfo>();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tenant_id, job_id, jira_connection_id
            FROM public.fn_list_stale_pending_backfill_jobs(@cutoff, @lim)
            """,
            conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        cmd.Parameters.AddWithValue("lim", limit);

        var results = new List<PendingBackfillPublishJobInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PendingBackfillPublishJobInfo(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2)));
        }

        return results;
    }
}
