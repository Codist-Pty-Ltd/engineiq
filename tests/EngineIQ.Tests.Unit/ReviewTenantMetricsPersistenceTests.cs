using EngineIQ.AIEngine;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIQ.Tests.Unit;

public class ReviewTenantMetricsPersistenceTests
{
    private sealed class RecordingTenantMetricsRepository : ITenantMetricsRepository
    {
        public int CallCount { get; private set; }
        public Guid? TenantId { get; private set; }
        public DateOnly? Date { get; private set; }
        public int? FindingsPersisted { get; private set; }
        public long? DurationMs { get; private set; }
        public decimal? TokenCostZar { get; private set; }

        public Task RecordJobCompletionAsync(
            Guid tenantId,
            DateOnly date,
            int findingsPersisted,
            long durationMs,
            decimal tokenCostZar,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            TenantId = tenantId;
            Date = date;
            FindingsPersisted = findingsPersisted;
            DurationMs = durationMs;
            TokenCostZar = tokenCostZar;
            return Task.CompletedTask;
        }

        public Task RecordIssueAnalysisCompletionAsync(
            Guid tenantId,
            DateOnly date,
            long durationMs,
            decimal tokenCostZar,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task TryRecordJobCompletionAsync_calls_repository_with_completion_inputs()
    {
        var repo = new RecordingTenantMetricsRepository();
        var tenantId = Guid.Parse("e2a5cfb5-5148-4737-9ee2-0c0f4d2093bf");
        var date = new DateOnly(2026, 6, 3);

        await ReviewTenantMetricsPersistence.TryRecordJobCompletionAsync(
            repo,
            tenantId,
            date,
            findingsPersisted: 3,
            durationMs: 4500,
            tokenCostZar: 2.10m,
            NullLogger.Instance);

        Assert.Equal(1, repo.CallCount);
        Assert.Equal(tenantId, repo.TenantId);
        Assert.Equal(date, repo.Date);
        Assert.Equal(3, repo.FindingsPersisted);
        Assert.Equal(4500, repo.DurationMs);
        Assert.Equal(2.10m, repo.TokenCostZar);
    }
}
