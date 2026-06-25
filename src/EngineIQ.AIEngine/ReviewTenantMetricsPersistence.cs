using EngineIQ.Domain.Interfaces;
using EngineIQ.Observability;
using Microsoft.Extensions.Logging;

namespace EngineIQ.AIEngine;

/// <summary>
/// Best-effort daily tenant_metrics rollup after a completed review. Failures are logged only.
/// </summary>
public static class ReviewTenantMetricsPersistence
{
    public static async Task TryRecordJobCompletionAsync(
        ITenantMetricsRepository metrics,
        Guid tenantId,
        DateOnly date,
        int findingsPersisted,
        long durationMs,
        decimal tokenCostZar,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await metrics.RecordJobCompletionAsync(
                tenantId,
                date,
                findingsPersisted,
                durationMs,
                tokenCostZar,
                cancellationToken);

            logger.LogInformation(
                "Updated tenant_metrics for tenant {TenantId} on {Date} (+1 PR, +{Findings} violations).",
                tenantId,
                date,
                findingsPersisted);
        }
        catch (Exception ex)
        {
            ReviewTelemetry.RecordPersistenceFailure("tenant_metrics");
            logger.LogWarning(
                ex,
                "Failed to update tenant_metrics for tenant {TenantId} on {Date}; job completion continues.",
                tenantId,
                date);
        }
    }
}
