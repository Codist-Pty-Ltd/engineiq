using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Admin.Services;

public sealed class AdminPortalService
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IJobRepository _jobs;
    private readonly IPullReviewJobPublisher _publisher;
    private readonly ILogger<AdminPortalService> _logger;

    public AdminPortalService(
        IDbContextFactory<EngineIQDbContext> factory,
        IJobRepository jobs,
        IPullReviewJobPublisher publisher,
        ILogger<AdminPortalService> logger)
    {
        _factory = factory;
        _jobs = jobs;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdminTenantRow>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        await using var root = await _factory.CreateDbContextAsync(cancellationToken);
        var tenants = await root.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var rows = new List<AdminTenantRow>();

        foreach (var t in tenants)
        {
            await using var scoped = await _factory.CreateDbContextAsync(cancellationToken);
            await scoped.SetCurrentTenantAsync(t.Id, cancellationToken);
            var prCount = await scoped.PrReviewJobs.LongCountAsync(cancellationToken);
            DateTimeOffset? lastActive = null;
            if (prCount > 0)
            {
                lastActive = await scoped.PrReviewJobs.MaxAsync(
                    j => j.CompletedAt ?? j.CreatedAt,
                    cancellationToken);
            }

            rows.Add(new AdminTenantRow(
                t.Id,
                t.Name,
                t.Plan,
                t.Status,
                prCount,
                lastActive,
                PlanMrrEstimator.MonthlyZar(t.Plan)));
        }

        return rows;
    }

    public async Task<bool> SetTenantSuspendedAsync(Guid tenantId, bool suspended, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return false;
        tenant.Status = suspended ? "Suspended" : "Active";
        await db.SaveChangesAsync(cancellationToken);
        if (suspended)
            _logger.LogWarning("Tenant {TenantId} suspended via admin portal.", tenantId);
        else
            _logger.LogInformation("Tenant {TenantId} resumed via admin portal.", tenantId);
        return true;
    }

    public async Task<bool> UpgradeTenantAsync(Guid tenantId, string plan, string? featureFlagsJson, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return false;
        tenant.Plan = plan.Trim();
        if (featureFlagsJson is not null)
            tenant.FeatureFlagsJson = string.IsNullOrWhiteSpace(featureFlagsJson) ? null : featureFlagsJson.Trim();
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Tenant {TenantId} plan updated to {Plan}.", tenantId, plan);
        return true;
    }

    public async Task<AdminTenantDetail?> GetTenantDetailAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var root = await _factory.CreateDbContextAsync(cancellationToken);
        var t = await root.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null)
            return null;

        await using var scoped = await _factory.CreateDbContextAsync(cancellationToken);
        await scoped.SetCurrentTenantAsync(t.Id, cancellationToken);
        var prCount = await scoped.PrReviewJobs.LongCountAsync(cancellationToken);
        DateTimeOffset? lastActive = null;
        if (prCount > 0)
        {
            lastActive = await scoped.PrReviewJobs.MaxAsync(
                j => j.CompletedAt ?? j.CreatedAt,
                cancellationToken);
        }

        return new AdminTenantDetail(
            t.Id,
            t.Name,
            t.Plan,
            t.Status,
            t.ContactEmail,
            t.CreatedAt,
            t.GitHubOrgLogin,
            t.GitHubAppInstallationId,
            t.FeatureFlagsJson,
            prCount,
            lastActive,
            PlanMrrEstimator.MonthlyZar(t.Plan));
    }

    public async Task<IReadOnlyList<AdminFindingRow>> ListFindingsAsync(Guid tenantId, int take = 500, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.Findings.AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .Select(f => new AdminFindingRow(
                f.Id,
                f.JobId,
                f.Severity,
                f.Category,
                f.RuleId,
                f.Source,
                f.FilePath,
                f.LineNumber,
                f.Message,
                f.WasActioned,
                f.PrMergeStatus,
                f.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminPlatformMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        await using var root = await _factory.CreateDbContextAsync(cancellationToken);
        var tenantIds = await root.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        long prsToday = 0;
        var durations = new List<long>();
        decimal tokenZarToday = 0;
        decimal mrrTotal = 0;

        foreach (var id in tenantIds)
        {
            await using var scoped = await _factory.CreateDbContextAsync(cancellationToken);
            await scoped.SetCurrentTenantAsync(id, cancellationToken);

            var tenantPlan = await scoped.Tenants.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => t.Plan)
                .FirstAsync(cancellationToken);
            mrrTotal += PlanMrrEstimator.MonthlyZar(tenantPlan);

            prsToday += await scoped.PrReviewJobs.LongCountAsync(
                j => j.Status == ReviewJobStatuses.Completed
                     && j.CompletedAt.HasValue
                     && DateOnly.FromDateTime(j.CompletedAt!.Value.UtcDateTime) == today,
                cancellationToken);

            var todayDurations = await scoped.PrReviewJobs.AsNoTracking()
                .Where(j => j.Status == ReviewJobStatuses.Completed
                            && j.CompletedAt.HasValue
                            && j.DurationMs.HasValue
                            && DateOnly.FromDateTime(j.CompletedAt!.Value.UtcDateTime) == today)
                .Select(j => j.DurationMs!.Value)
                .ToListAsync(cancellationToken);
            durations.AddRange(todayDurations);

            tokenZarToday += await scoped.TenantMetrics.AsNoTracking()
                .Where(m => m.Date == today)
                .Select(m => m.TokenCostZar)
                .FirstOrDefaultAsync(cancellationToken);
        }

        double? avgMs = durations.Count == 0 ? null : durations.Average();

        return new AdminPlatformMetrics(
            prsToday,
            avgMs,
            tokenZarToday,
            mrrTotal);
    }

    public async Task<IReadOnlyList<AdminFailedJobRow>> ListFailedJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var root = await _factory.CreateDbContextAsync(cancellationToken);
        var tenantIds = await root.Tenants.AsNoTracking().Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
        var rows = new List<AdminFailedJobRow>();

        foreach (var t in tenantIds)
        {
            await using var scoped = await _factory.CreateDbContextAsync(cancellationToken);
            await scoped.SetCurrentTenantAsync(t.Id, cancellationToken);
            var failed = await scoped.PrReviewJobs.AsNoTracking()
                .Include(j => j.Repository)
                .Where(j => j.Status == ReviewJobStatuses.Failed)
                .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
                .Take(200)
                .Select(j => new AdminFailedJobRow(
                    t.Id,
                    t.Name,
                    j.Id,
                    j.Repository != null ? j.Repository.FullName : "",
                    j.PrNumber,
                    j.CompletedAt ?? j.CreatedAt))
                .ToListAsync(cancellationToken);
            rows.AddRange(failed);
        }

        return rows.OrderByDescending(r => r.When).Take(500).ToList();
    }

    public async Task RetryFailedDbJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var info = await _jobs.TryResetFailedJobToQueuedAsync(tenantId, jobId, cancellationToken);
        if (info is null)
            throw new InvalidOperationException("Job not found or not in Failed state.");

        var msg = new PullReviewJobMessage(
            info.TenantId,
            info.JobId,
            info.RepositoryId,
            info.InstallationId,
            info.Owner,
            info.Repo,
            info.PrNumber,
            Attempt: 0);

        await _publisher.PublishAsync(msg, cancellationToken);
        _logger.LogInformation("Re-published failed job {JobId} for tenant {TenantId}.", jobId, tenantId);
    }
}

public sealed record AdminTenantRow(
    Guid Id,
    string Name,
    string Plan,
    string Status,
    long PrCount,
    DateTimeOffset? LastActive,
    decimal MrrContributionZar);

public sealed record AdminTenantDetail(
    Guid Id,
    string Name,
    string Plan,
    string Status,
    string? ContactEmail,
    DateTimeOffset CreatedAt,
    string? GitHubOrgLogin,
    long? GitHubAppInstallationId,
    string? FeatureFlagsJson,
    long PrCount,
    DateTimeOffset? LastActive,
    decimal MrrContributionZar);

public sealed record AdminFindingRow(
    Guid Id,
    Guid JobId,
    string Severity,
    string Category,
    string? RuleId,
    string Source,
    string FilePath,
    int? LineNumber,
    string Message,
    bool WasActioned,
    string PrMergeStatus,
    DateTimeOffset CreatedAt);

public sealed record AdminPlatformMetrics(
    long PrsReviewedToday,
    double? AvgReviewLatencyMsToday,
    decimal TokenCostZarToday,
    decimal RevenueMrrZar);

public sealed record AdminFailedJobRow(
    Guid TenantId,
    string TenantName,
    Guid JobId,
    string RepositoryFullName,
    int PrNumber,
    DateTimeOffset When);
