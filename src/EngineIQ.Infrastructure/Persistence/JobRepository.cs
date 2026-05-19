using EngineIQ.Domain.Audit;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jobs;
using EngineIQ.Domain.Persistence;
using EngineIQ.Domain.Tenants;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class JobRepository : IJobRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IOptions<PostgresOptions> _options;

    public JobRepository(IDbContextFactory<EngineIQDbContext> factory, IOptions<PostgresOptions> options)
    {
        _factory = factory;
        _options = options;
    }

    public async Task<PrJobEnqueueResult> TryCreateQueuedJobAsync(
        long githubAppInstallationId,
        string repositoryFullName,
        int prNumber,
        string githubDeliveryId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveTenantIdByInstallationAsync(githubAppInstallationId, cancellationToken);
        if (tenantId is null)
            return new PrJobEnqueueResult(false, null, null, null, githubAppInstallationId);

        await using (var gate = await _factory.CreateDbContextAsync(cancellationToken))
        {
            var tenantStatus = await gate.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId.Value)
                .Select(t => t.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.Equals(tenantStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
                return new PrJobEnqueueResult(false, tenantId, null, null, githubAppInstallationId, "suspended");
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SetCurrentTenantAsync(tenantId.Value, cancellationToken);

            try
            {
                var repository = await db.Repositories
                    .FirstOrDefaultAsync(r => r.FullName == repositoryFullName, cancellationToken);

                if (repository is null)
                {
                    repository = new Repository
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        FullName = repositoryFullName
                    };
                    db.Repositories.Add(repository);
                    await db.SaveChangesAsync(cancellationToken);
                }

                var job = new PrReviewJob
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    RepositoryId = repository.Id,
                    PrNumber = prNumber,
                    GithubDeliveryId = githubDeliveryId,
                    Status = ReviewJobStatuses.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                db.PrReviewJobs.Add(job);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new PrJobEnqueueResult(true, tenantId, repository.Id, job.Id, githubAppInstallationId);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                if (await IsDeliveryAlreadyQueuedAsync(tenantId.Value, githubDeliveryId, cancellationToken))
                    return new PrJobEnqueueResult(false, tenantId, null, null, githubAppInstallationId, "duplicate");

                // Repository (tenant_id, full_name) race — retry
            }
        }

        return new PrJobEnqueueResult(false, tenantId, null, null, githubAppInstallationId, "enqueue_failed");
    }

    private async Task<bool> IsDeliveryAlreadyQueuedAsync(Guid tenantId, string githubDeliveryId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.PrReviewJobs.AnyAsync(j => j.GithubDeliveryId == githubDeliveryId, cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private async Task<Guid?> ResolveTenantIdByInstallationAsync(long installationId, CancellationToken cancellationToken)
    {
        var cs = _options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Postgres:ConnectionString is not configured.");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT public.fn_resolve_tenant_by_installation(@i)",
            conn);
        cmd.Parameters.AddWithValue("i", installationId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid g ? g : null;
    }

    public async Task MarkJobProcessingAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.PrReviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return;
        job.Status = ReviewJobStatuses.Processing;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int findingsCount,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostZar,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.PrReviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return;
        job.Status = ReviewJobStatuses.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        job.FindingsCount = findingsCount;
        job.InputTokens = inputTokens;
        job.OutputTokens = outputTokens;
        job.EstimatedCostZar = estimatedCostZar;
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
        var job = await db.PrReviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
            return;

        job.Status = ReviewJobStatuses.Skipped;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = 0;
        job.FindingsCount = 0;
        job.InputTokens = 0;
        job.OutputTokens = 0;
        job.EstimatedCostZar = 0m;
        // GithubDeliveryId is unique per delivery; skip reason is logged only (not persisted on job row).
        _ = skipReason;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<TenantAuditReviewRow> Items, int TotalCount)> ListAuditReviewsAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var baseQuery = db.PrReviewJobs.AsNoTracking()
            .Where(j => j.TenantId == tenantId && j.Status == ReviewJobStatuses.Completed && j.CompletedAt != null);

        var total = await baseQuery.CountAsync(cancellationToken);

        var jobs = await baseQuery
            .OrderByDescending(j => j.CompletedAt)
            .Skip(skip)
            .Take(take)
            .Include(j => j.Repository)
            .ToListAsync(cancellationToken);

        var rows = jobs
            .Where(j => j.Repository is not null)
            .Select(j => new TenantAuditReviewRow(
                j.CompletedAt!.Value,
                j.PrNumber,
                j.Repository!.FullName,
                j.FindingsCount,
                j.DurationMs,
                j.EstimatedCostZar,
                j.InputTokens,
                j.OutputTokens))
            .ToList();

        return (rows, total);
    }

    public async Task<(IReadOnlyList<TenantPrJobRow> Items, int TotalCount)> ListTenantJobsAsync(
        Guid tenantId,
        string? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var q = db.PrReviewJobs.AsNoTracking().Where(j => j.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim();
            q = q.Where(j => j.Status == s);
        }

        var total = await q.CountAsync(cancellationToken);
        var jobs = await q
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Include(j => j.Repository)
            .ToListAsync(cancellationToken);

        var rows = new List<TenantPrJobRow>(jobs.Count);
        foreach (var j in jobs)
        {
            if (j.Repository is null)
                continue;
            rows.Add(ToTenantJobRow(j, j.Repository.FullName));
        }

        return (rows, total);
    }

    public async Task<TenantPrJobRow?> GetTenantJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var j = await db.PrReviewJobs.AsNoTracking()
            .Include(x => x.Repository)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == jobId, cancellationToken);
        if (j?.Repository is null)
            return null;
        return ToTenantJobRow(j, j.Repository.FullName);
    }

    public async Task<TenantUsageSummary?> GetTenantUsageSummaryAsync(
        Guid tenantId,
        int days,
        CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 366);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var exists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken);
        if (!exists)
            return null;

        var q = db.PrReviewJobs.AsNoTracking()
            .Where(j =>
                j.TenantId == tenantId
                && j.Status == ReviewJobStatuses.Completed
                && j.CompletedAt != null
                && j.CompletedAt >= cutoff);

        var completed = await q.CountAsync(cancellationToken);
        var tokensIn = await q.SumAsync(j => (long)j.InputTokens, cancellationToken);
        var tokensOut = await q.SumAsync(j => (long)j.OutputTokens, cancellationToken);
        var cost = await q.SumAsync(j => j.EstimatedCostZar ?? 0m, cancellationToken);

        return new TenantUsageSummary(days, completed, tokensIn, tokensOut, cost);
    }

    private static TenantPrJobRow ToTenantJobRow(PrReviewJob j, string repositoryFullName) =>
        new(
            j.Id,
            repositoryFullName,
            j.PrNumber,
            j.Status,
            j.CreatedAt,
            j.CompletedAt,
            j.DurationMs,
            j.FindingsCount,
            j.InputTokens,
            j.OutputTokens,
            j.EstimatedCostZar);

    public async Task MarkJobFailedAsync(Guid tenantId, Guid jobId, long? durationMs, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.PrReviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return;
        job.Status = ReviewJobStatuses.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.DurationMs = durationMs;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteQueuedJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.PrReviewJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return;
        db.PrReviewJobs.Remove(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FailedJobRetryInfo?> TryResetFailedJobToQueuedAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var job = await db.PrReviewJobs
            .Include(j => j.Repository)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.Repository is null || job.Status != ReviewJobStatuses.Failed)
            return null;

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant?.GitHubAppInstallationId is null)
            return null;

        var (owner, repo) = ParseOwnerRepo(job.Repository.FullName);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            return null;

        job.Status = ReviewJobStatuses.Queued;
        job.CompletedAt = null;
        job.DurationMs = null;
        await db.SaveChangesAsync(cancellationToken);

        return new FailedJobRetryInfo(
            tenantId,
            jobId,
            job.RepositoryId,
            tenant.GitHubAppInstallationId.Value,
            owner,
            repo,
            job.PrNumber);
    }

    public async Task<IReadOnlyList<PortalNotificationItem>> ListPortalNotificationsAsync(
        Guid tenantId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return Array.Empty<PortalNotificationItem>();

        var items = new List<PortalNotificationItem>();

        if (tenant.GitHubAppInstallationId.HasValue)
        {
            items.Add(new PortalNotificationItem(
                "github_connected",
                "GitHub App connected",
                $"{tenant.GitHubOrgLogin ?? "Your organisation"} · EngineIQ can receive pull request events",
                tenant.CreatedAt,
                null));
        }

        var queued = await db.PrReviewJobs.AsNoTracking()
            .Include(j => j.Repository)
            .Where(j =>
                j.TenantId == tenantId
                && j.Status == ReviewJobStatuses.Queued
                && j.CreatedAt >= cutoff
                && j.Repository != null)
            .OrderByDescending(j => j.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var j in queued)
        {
            items.Add(new PortalNotificationItem(
                "pr_queued",
                "Pull request queued for review",
                $"{j.Repository!.FullName} · PR #{j.PrNumber}",
                j.CreatedAt,
                j.Id));
        }

        var skipped = await db.PrReviewJobs.AsNoTracking()
            .Include(j => j.Repository)
            .Where(j =>
                j.TenantId == tenantId
                && j.Status == ReviewJobStatuses.Skipped
                && j.Repository != null
                && (j.CompletedAt ?? j.CreatedAt) >= cutoff)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var j in skipped)
        {
            items.Add(new PortalNotificationItem(
                "review_skipped",
                "Review skipped",
                $"{j.Repository!.FullName} · PR #{j.PrNumber} (tenant preferences)",
                j.CompletedAt ?? j.CreatedAt,
                j.Id));
        }

        var completed = await db.PrReviewJobs.AsNoTracking()
            .Include(j => j.Repository)
            .Where(j =>
                j.TenantId == tenantId
                && (j.Status == ReviewJobStatuses.Completed || j.Status == ReviewJobStatuses.Failed)
                && j.Repository != null
                && (j.CompletedAt ?? j.CreatedAt) >= cutoff)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var j in completed)
        {
            var at = j.CompletedAt ?? j.CreatedAt;
            var issueLabel = j.FindingsCount == 1 ? "issue" : "issues";
            var title = j.Status == ReviewJobStatuses.Failed ? "Review failed" : "Review complete";
            var sub = j.Status == ReviewJobStatuses.Failed
                ? $"{j.Repository!.FullName} · PR #{j.PrNumber}"
                : $"{j.Repository!.FullName} · PR #{j.PrNumber} — {j.FindingsCount} {issueLabel} found";
            items.Add(new PortalNotificationItem(
                j.Status == ReviewJobStatuses.Failed ? "review_failed" : "review_complete",
                title,
                sub,
                at,
                j.Id));
        }

        var criticalFindings = await db.Findings.AsNoTracking()
            .Where(f =>
                f.TenantId == tenantId
                && f.CreatedAt >= cutoff
                && f.Severity.ToLower() == "critical")
            .OrderByDescending(f => f.CreatedAt)
            .Take(20)
            .Select(f => new { f.Id, f.JobId, f.CreatedAt, f.FilePath, f.RuleId })
            .ToListAsync(cancellationToken);

        if (criticalFindings.Count > 0)
        {
            var jobIds = criticalFindings.Select(f => f.JobId).Distinct().ToList();
            var jobMeta = await db.PrReviewJobs.AsNoTracking()
                .Include(j => j.Repository)
                .Where(j => jobIds.Contains(j.Id) && j.Repository != null)
                .Select(j => new { j.Id, j.PrNumber, Repo = j.Repository!.FullName })
                .ToDictionaryAsync(j => j.Id, cancellationToken);

            foreach (var f in criticalFindings)
            {
                if (!jobMeta.TryGetValue(f.JobId, out var meta))
                    continue;
                var loc = string.IsNullOrWhiteSpace(f.FilePath) ? "finding" : f.FilePath;
                items.Add(new PortalNotificationItem(
                    "critical_issue",
                    "Critical issue found",
                    $"{meta.Repo} · PR #{meta.PrNumber} — {loc}",
                    f.CreatedAt,
                    f.JobId));
            }
        }

        return items
            .OrderByDescending(i => i.OccurredAt)
            .Take(take)
            .ToList();
    }

    private static (string Owner, string Repo) ParseOwnerRepo(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return ("", "");
        return (parts[0], parts[1]);
    }
}
