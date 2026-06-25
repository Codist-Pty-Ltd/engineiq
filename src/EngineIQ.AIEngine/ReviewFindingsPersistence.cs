using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EngineIQ.AIEngine;

/// <summary>
/// Best-effort persistence of parsed review findings. Failures are logged and do not fail the review job.
/// </summary>
public static class ReviewFindingsPersistence
{
    public static async Task TryPersistAsync(
        IFindingRepository findings,
        Guid tenantId,
        Guid jobId,
        IReadOnlyList<FindingWriteDto> parsed,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (parsed.Count == 0)
            return;

        try
        {
            await findings.AddFindingsAsync(tenantId, jobId, parsed, cancellationToken);
            logger.LogInformation(
                "Persisted {Count} findings for tenant {TenantId} job {JobId}.",
                parsed.Count,
                tenantId,
                jobId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist {Count} findings for tenant {TenantId} job {JobId}; review comment was still posted.",
                parsed.Count,
                tenantId,
                jobId);
        }
    }
}
