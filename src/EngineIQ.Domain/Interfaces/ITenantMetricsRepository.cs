namespace EngineIQ.Domain.Interfaces;

/// <summary>Daily per-tenant usage rollups in <c>tenant_metrics</c>.</summary>
public interface ITenantMetricsRepository
{
    /// <summary>
    /// Upserts the UTC-day row for <paramref name="tenantId"/>: +1 PR reviewed,
    /// +findings violations, running average review ms, +token cost ZAR.
    /// </summary>
    Task RecordJobCompletionAsync(
        Guid tenantId,
        DateOnly date,
        int findingsPersisted,
        long durationMs,
        decimal tokenCostZar,
        CancellationToken cancellationToken = default);
}
