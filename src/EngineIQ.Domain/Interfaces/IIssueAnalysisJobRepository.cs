namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Jira issue analysis jobs and enqueue idempotency. Mutating operations require explicit tenant scope.
/// </summary>
public interface IIssueAnalysisJobRepository
{
    /// <summary>
    /// Inserts job in PendingPublish. Duplicate <paramref name="dedupeKey"/> returns Created=false;
    /// if the existing row is still PendingPublish, NeedsRepublish is true.
    /// </summary>
    Task<IssueAnalysisJobEnqueueResult> TryCreateQueuedJobAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string issueKey,
        long jiraIssueId,
        string dedupeKey,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkJobProcessingIfQueuedAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    Task<bool> MarkJobQueuedAfterPublishAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingJiraPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int inputTokens,
        int outputTokens,
        decimal estimatedCostZar,
        CancellationToken cancellationToken = default);

    Task MarkJobFailedAsync(
        Guid tenantId,
        Guid jobId,
        string? failureReason,
        long? durationMs,
        CancellationToken cancellationToken = default);

    Task MarkJobSkippedAsync(
        Guid tenantId,
        Guid jobId,
        string skipReason,
        CancellationToken cancellationToken = default);
}

public sealed record IssueAnalysisJobEnqueueResult(
    bool Created,
    Guid? TenantId,
    Guid? JobId,
    Guid? JiraConnectionId,
    string? BlockReason = null,
    bool NeedsRepublish = false);

public sealed record PendingJiraPublishJobInfo(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId,
    string IssueKey,
    long JiraIssueId);

public interface IJiraIssueAnalysisJobPublisher
{
    Task PublishAsync(Messaging.JiraIssueAnalysisJobMessage job, CancellationToken cancellationToken = default);
}
