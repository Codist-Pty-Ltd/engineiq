using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Metrics;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class TenantMetricsRepository : ITenantMetricsRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;

    public TenantMetricsRepository(IDbContextFactory<EngineIQDbContext> factory)
    {
        _factory = factory;
    }

    public async Task RecordJobCompletionAsync(
        Guid tenantId,
        DateOnly date,
        int findingsPersisted,
        long durationMs,
        decimal tokenCostZar,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var row = await db.TenantMetrics
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Date == date, cancellationToken);

        var existing = row is null
            ? new TenantMetricsSnapshot(0, 0, 0, 0m)
            : new TenantMetricsSnapshot(row.PrsReviewed, row.ViolationsFound, row.AvgReviewMs, row.TokenCostZar);

        var updated = TenantMetricsAggregation.ApplyJobCompletion(
            existing,
            findingsPersisted,
            durationMs,
            tokenCostZar);

        if (row is null)
        {
            row = new TenantMetric
            {
                TenantId = tenantId,
                Date = date
            };
            db.TenantMetrics.Add(row);
        }

        row.PrsReviewed = updated.PrsReviewed;
        row.ViolationsFound = updated.ViolationsFound;
        row.AvgReviewMs = updated.AvgReviewMs;
        row.TokenCostZar = updated.TokenCostZar;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordIssueAnalysisCompletionAsync(
        Guid tenantId,
        DateOnly date,
        long durationMs,
        decimal tokenCostZar,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var row = await db.TenantMetrics
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Date == date, cancellationToken);

        if (row is null)
        {
            row = new TenantMetric
            {
                TenantId = tenantId,
                Date = date,
                IssuesAnalyzed = 1,
                TokenCostZar = tokenCostZar,
            };
            db.TenantMetrics.Add(row);
        }
        else
        {
            row.IssuesAnalyzed += 1;
            row.TokenCostZar += tokenCostZar;
        }

        _ = durationMs;
        await db.SaveChangesAsync(cancellationToken);
    }
}
