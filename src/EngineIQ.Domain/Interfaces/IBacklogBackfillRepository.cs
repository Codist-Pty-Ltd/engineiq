using EngineIQ.Domain.Messaging;

namespace EngineIQ.Domain.Interfaces;

public interface IBacklogBackfillRepository
{
    Task<BacklogBackfillEnqueueResult> TryCreateQueuedAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string jql,
        int maxIssues,
        CancellationToken cancellationToken = default);

    Task<Guid?> FindActiveJobIdAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        CancellationToken cancellationToken = default);

    Task<BacklogBackfillRow?> GetByIdAsync(
        Guid tenantId,
        Guid backfillId,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkProcessingIfQueuedAsync(
        Guid tenantId,
        Guid backfillId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkQueuedAfterPublishAsync(
        Guid tenantId,
        Guid backfillId,
        CancellationToken cancellationToken = default);

    Task UpdateProgressAsync(
        Guid tenantId,
        Guid backfillId,
        int startAtCursor,
        int matchedTotal,
        int enqueuedCount,
        int skippedCount,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid tenantId,
        Guid backfillId,
        int matchedTotal,
        int enqueuedCount,
        int skippedCount,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid tenantId,
        Guid backfillId,
        string? failureReason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingBackfillPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public sealed record BacklogBackfillEnqueueResult(
    bool Created,
    Guid? TenantId,
    Guid? JobId,
    string? BlockReason = null,
    bool NeedsRepublish = false);

public sealed record BacklogBackfillRow(
    Guid Id,
    Guid TenantId,
    Guid JiraConnectionId,
    string Jql,
    string Status,
    int StartAtCursor,
    int MatchedTotal,
    int EnqueuedCount,
    int SkippedCount,
    int MaxIssues,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

public sealed record PendingBackfillPublishJobInfo(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId);

public interface IBacklogBackfillJobPublisher
{
    Task PublishAsync(BacklogBackfillJobMessage job, CancellationToken cancellationToken = default);
}
