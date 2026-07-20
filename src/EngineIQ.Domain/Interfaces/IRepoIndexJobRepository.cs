namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Repository code-index jobs and enqueue idempotency. All mutating operations require explicit tenant scope.
/// </summary>
public interface IRepoIndexJobRepository
{
    /// <summary>
    /// Inserts job in PendingPublish. Duplicate <paramref name="dedupeKey"/> returns Created=false;
    /// if the existing row is still PendingPublish, NeedsRepublish is true.
    /// </summary>
    Task<RepoIndexJobEnqueueResult> TryCreateQueuedJobAsync(
        Guid tenantId,
        Guid repositoryId,
        long installationId,
        string owner,
        string repo,
        string headSha,
        string? baseSha,
        string dedupeKey,
        CancellationToken cancellationToken = default);

    /// <summary>True when the repository already has a job in PendingPublish/Queued/Processing (used to reject concurrent manual triggers with 409).</summary>
    Task<Guid?> FindActiveJobIdForRepoAsync(
        Guid tenantId,
        Guid repositoryId,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkJobProcessingIfQueuedAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>After a successful RabbitMQ publish, moves PendingPublish → Queued.</summary>
    Task<bool> MarkJobQueuedAfterPublishAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingRepoIndexPublishJobInfo>> ListStalePendingPublishJobsAsync(
        TimeSpan staleOlderThan,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task MarkJobCompletedAsync(
        Guid tenantId,
        Guid jobId,
        long durationMs,
        int filesWalked,
        int chunksTotal,
        int chunksEmbedded,
        int chunksDeleted,
        CancellationToken cancellationToken = default);

    Task MarkJobFailedAsync(
        Guid tenantId,
        Guid jobId,
        string? failureReason,
        long? durationMs,
        CancellationToken cancellationToken = default);
}

public sealed record RepoIndexJobEnqueueResult(
    bool Created,
    Guid? TenantId,
    Guid? JobId,
    string? BlockReason = null,
    bool NeedsRepublish = false);

public sealed record PendingRepoIndexPublishJobInfo(
    Guid TenantId,
    Guid JobId,
    Guid RepositoryId,
    long InstallationId,
    string Owner,
    string Repo,
    string HeadSha,
    string? BaseSha);

public interface IRepoIndexJobPublisher
{
    Task PublishAsync(Messaging.RepoIndexJobMessage job, CancellationToken cancellationToken = default);
}
