using EngineIQ.Domain.Metrics;

namespace EngineIQ.Tests.Unit;

public class TenantMetricsAggregationTests
{
    [Fact]
    public void ApplyJobCompletion_on_empty_row_sets_first_pr_average_and_cost()
    {
        var empty = new TenantMetricsSnapshot(0, 0, 0, 0m);

        var result = TenantMetricsAggregation.ApplyJobCompletion(empty, findingsPersisted: 2, durationMs: 1200, tokenCostZar: 1.50m);

        Assert.Equal(1, result.PrsReviewed);
        Assert.Equal(2, result.ViolationsFound);
        Assert.Equal(1200, result.AvgReviewMs);
        Assert.Equal(1.50m, result.TokenCostZar);
    }

    [Fact]
    public void ApplyJobCompletion_increments_running_average_and_accumulates_cost()
    {
        var existing = new TenantMetricsSnapshot(PrsReviewed: 2, ViolationsFound: 3, AvgReviewMs: 1000, TokenCostZar: 2m);

        var result = TenantMetricsAggregation.ApplyJobCompletion(existing, findingsPersisted: 1, durationMs: 1600, tokenCostZar: 0.75m);

        Assert.Equal(3, result.PrsReviewed);
        Assert.Equal(4, result.ViolationsFound);
        Assert.Equal((1000d * 2 + 1600) / 3, result.AvgReviewMs);
        Assert.Equal(2.75m, result.TokenCostZar);
    }

    [Fact]
    public void ApplyJobCompletion_counts_pr_even_when_zero_findings()
    {
        var existing = new TenantMetricsSnapshot(1, 5, 900, 1m);

        var result = TenantMetricsAggregation.ApplyJobCompletion(existing, findingsPersisted: 0, durationMs: 300, tokenCostZar: 0.25m);

        Assert.Equal(2, result.PrsReviewed);
        Assert.Equal(5, result.ViolationsFound);
        Assert.Equal((900d + 300) / 2, result.AvgReviewMs);
        Assert.Equal(1.25m, result.TokenCostZar);
    }
}
