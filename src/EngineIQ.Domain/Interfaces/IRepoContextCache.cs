using EngineIQ.Domain.Context;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Redis cache for <see cref="RepoContext"/> keyed by tenant + repository.</summary>
public interface IRepoContextCache
{
    string BuildCacheKey(Guid tenantId, string owner, string repo);

    Task<RepoContext?> TryGetAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task SetAsync(
        string cacheKey,
        RepoContext context,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
