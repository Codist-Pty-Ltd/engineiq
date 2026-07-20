namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Repository row lookups/updates for code indexing. Distinct from <see cref="ITenantRepository"/>'s
/// portal-facing repository list; this is the indexing-focused surface (installation resolution, index state).
/// </summary>
public interface IRepositoryRepository
{
    Task<RepositoryLookupRow?> GetByIdAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the tenant by GitHub App installation id (excludes Suspended) and gets-or-creates the
    /// repository row by full name — mirrors <c>IJobRepository.TryCreateQueuedJobAsync</c>'s upsert so a
    /// repository can be indexed from its first push, before any PR has ever been opened.
    /// </summary>
    Task<RepositoryInstallationLookup?> TryResolveByInstallationAndFullNameAsync(
        long installationId,
        string fullName,
        CancellationToken cancellationToken = default);

    Task SetIndexStateAsync(
        Guid tenantId,
        Guid repositoryId,
        string commitSha,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default);
}

public sealed record RepositoryLookupRow(
    Guid Id,
    Guid TenantId,
    string FullName,
    string? IndexedCommitSha,
    long InstallationId);

public sealed record RepositoryInstallationLookup(
    Guid TenantId,
    Guid RepositoryId,
    string? IndexedCommitSha,
    string TenantStatus);
