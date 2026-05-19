using EngineIQ.Domain.Audit;
using EngineIQ.Domain.Jobs;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// PR review jobs and enqueue idempotency. All mutating operations require explicit tenant scope.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Resolves tenant by GitHub App installation id, upserts repository, inserts queued job.
    /// Duplicate (tenant_id, github_delivery_id) returns Created=false.
    /// </summary>
    Task<PrJobEnqueueResult> TryCreateQueuedJobAsync(
        long githubAppInstallationId,
        string repositoryFullName,
        int prNumber,
        string githubDeliveryId,
        CancellationToken cancellationToken = default);

    Task MarkJobProcessingAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int findingsCount,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostZar,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a job skipped per tenant preferences (no AI call, no GitHub comment).</summary>
    Task MarkJobSkippedAsync(
        Guid tenantId,
        Guid jobId,
        string skipReason,
        CancellationToken cancellationToken = default);

    /// <summary>Completed reviews only — metadata for compliance audit (no messages or code).</summary>
    Task<(IReadOnlyList<TenantAuditReviewRow> Items, int TotalCount)> ListAuditReviewsAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>All jobs for tenant portal (queued/processing/completed/failed). Optional status filter.</summary>
    Task<(IReadOnlyList<TenantPrJobRow> Items, int TotalCount)> ListTenantJobsAsync(
        Guid tenantId,
        string? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<TenantPrJobRow?> GetTenantJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    Task<TenantUsageSummary?> GetTenantUsageSummaryAsync(
        Guid tenantId,
        int days,
        CancellationToken cancellationToken = default);

    /// <summary>Activity feed for the portal notifications screen (derived from jobs and findings metadata).</summary>
    Task<IReadOnlyList<PortalNotificationItem>> ListPortalNotificationsAsync(
        Guid tenantId,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task MarkJobFailedAsync(Guid tenantId, Guid jobId, long? durationMs, CancellationToken cancellationToken = default);

    /// <summary>Removes queued job row when RabbitMQ publish fails after insert (webhook rollback).</summary>
    Task DeleteQueuedJobAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Admin: if job status is Failed, reset to Queued and return payload for republish.</summary>
    Task<FailedJobRetryInfo?> TryResetFailedJobToQueuedAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);
}

public sealed record PrJobEnqueueResult(
    bool Created,
    Guid? TenantId,
    Guid? RepositoryId,
    Guid? JobId,
    long GithubAppInstallationId,
    string? BlockReason = null);

public sealed record FailedJobRetryInfo(
    Guid TenantId,
    Guid JobId,
    Guid RepositoryId,
    long InstallationId,
    string Owner,
    string Repo,
    int PrNumber);
