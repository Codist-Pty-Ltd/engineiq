namespace EngineIQ.Domain.Metrics;

/// <summary>Daily tenant_metrics rollups (pure increment / running-average logic).</summary>
public static class TenantMetricsAggregation
{
    public static TenantMetricsSnapshot ApplyJobCompletion(
        TenantMetricsSnapshot existing,
        int findingsPersisted,
        long durationMs,
        decimal tokenCostZar)
    {
        var prsReviewed = existing.PrsReviewed + 1;
        var violationsFound = existing.ViolationsFound + Math.Max(0, findingsPersisted);
        var avgReviewMs = existing.PrsReviewed == 0
            ? durationMs
            : (existing.AvgReviewMs * existing.PrsReviewed + durationMs) / prsReviewed;
        var tokenCost = existing.TokenCostZar + Math.Max(0m, tokenCostZar);

        return new TenantMetricsSnapshot(prsReviewed, violationsFound, avgReviewMs, tokenCost);
    }
}

public sealed record TenantMetricsSnapshot(
    int PrsReviewed,
    int ViolationsFound,
    double AvgReviewMs,
    decimal TokenCostZar);
