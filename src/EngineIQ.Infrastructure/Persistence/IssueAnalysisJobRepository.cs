using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class IssueAnalysisJobRepository : IIssueAnalysisJobRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IOptions<PostgresOptions> _postgres;

    public IssueAnalysisJobRepository(
        IDbContextFactory<EngineIQDbContext> factory,
        IOptions<PostgresOptions> postgres)
    {
        _factory = factory;
        _postgres = postgres;
    }

    public async Task<IssueAnalysisJobEnqueueResult> TryCreateQueuedJobAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string issueKey,
        long jiraIssueId,
        string dedupeKey,
        CancellationToken cancellationToken = default,
        AnalysisTrigger trigger = AnalysisTrigger.Created)
    {
        await using (var gate = await _factory.CreateDbContextAsync(cancellationToken))
        {
            var tenantStatus = await gate.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.Equals(tenantStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                return new IssueAnalysisJobEnqueueResult(false, tenantId, null, jiraConnectionId, "suspended");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        try
        {
            var job = new IssueAnalysisJob
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                JiraConnectionId = jiraConnectionId,
                IssueKey = issueKey,
                JiraIssueId = jiraIssueId,
                DedupeKey = dedupeKey,
                Status = ReviewJobStatuses.PendingPublish,
                Attempt = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                Trigger = trigger.ToString(),
            };
            db.IssueAnalysisJobs.Add(job);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new IssueAnalysisJobEnqueueResult(true, tenantId, job.Id, jiraConnectionId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(cancellationToken);
            var duplicate = await TryResolveDuplicateAsync(tenantId, dedupeKey, jiraConnectionId, cancellationToken);
            return duplicate ?? new IssueAnalysisJobEnqueueResult(false, tenantId, null, jiraConnectionId, "enqueue_failed");
        }
    }

    private async Task<IssueAnalysisJobEnqueueResult?> TryResolveDuplicateAsync(
        Guid tenantId,
        string dedupeKey,
        Guid jiraConnectionId,
        CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var existing = await db.IssueAnalysisJobs.AsNoTracking()
            .Where(j => j.DedupeKey == dedupeKey)
            .Select(j => new { j.Id, j.JiraConnectionId, j.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
            return null;

        if (string.Equals(existing.Status, ReviewJobStatuses.PendingPublish, StringComparison.Ordinal))
        {
            return new IssueAnalysisJobEnqueueResult(
                false,
                tenantId,
                existing.Id,
                existing.JiraConnectionId,
                NeedsRepublish: true);
        }

        return new IssueAnalysisJobEnqueueResult(false, tenantId, null, jiraConnectionId, "duplicate");
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

        var updated = await db.IssueAnalysisJobs
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
        var updated = await db.IssueAnalysisJobs
            .Where(j => j.TenantId == tenantId && j.Id == jobId && j.Status == ReviewJobStatuses.PendingPublish)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, ReviewJobStatuses.Queued),
                cancellationToken);
        return updated > 0;
    }

    public async Task<IReadOnlyList<PendingJiraPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var cutoff = DateTimeOffset.UtcNow - staleOlderThan;
        var cs = _postgres.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            return Array.Empty<PendingJiraPublishJobInfo>();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tenant_id, job_id, jira_connection_id, issue_key, jira_issue_id, "trigger"
            FROM public.fn_list_stale_pending_jira_jobs(@cutoff, @lim)
            """,
            conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        cmd.Parameters.AddWithValue("lim", limit);

        var results = new List<PendingJiraPublishJobInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var trigger = AnalysisTrigger.Created;
            if (!reader.IsDBNull(5))
            {
                var triggerText = reader.GetString(5);
                if (!Enum.TryParse(triggerText, ignoreCase: true, out trigger))
                    trigger = AnalysisTrigger.Created;
            }

            results.Add(new PendingJiraPublishJobInfo(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt64(4),
                trigger));
        }

        return results;
    }

    public async Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostZar,
        CancellationToken cancellationToken = default,
        int reposSearched = 0,
        int chunksRetrieved = 0)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.IssueAnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        job.InputTokens = inputTokens;
        job.OutputTokens = outputTokens;
        job.EstimatedCostZar = estimatedCostZar;
        job.ReposSearched = reposSearched;
        job.ChunksRetrieved = chunksRetrieved;
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
        var job = await db.IssueAnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        job.FailureReason = failureReason;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkJobSkippedAsync(
        Guid tenantId,
        Guid jobId,
        string skipReason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.IssueAnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;
        job.Status = ReviewJobStatuses.Skipped;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = 0;
        job.FailureReason = skipReason;
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
