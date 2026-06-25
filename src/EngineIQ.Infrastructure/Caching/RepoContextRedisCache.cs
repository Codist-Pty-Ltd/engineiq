using System.Text.Json;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EngineIQ.Infrastructure.Caching;

public sealed class RepoContextRedisCache : IRepoContextCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RepoContextRedisCache> _logger;

    public RepoContextRedisCache(IConnectionMultiplexer redis, ILogger<RepoContextRedisCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public string BuildCacheKey(Guid tenantId, string owner, string repo) =>
        $"engineiq:repo-context:{tenantId}:{owner.Trim().ToLowerInvariant()}/{repo.Trim().ToLowerInvariant()}";

    public async Task<RepoContext?> TryGetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(cacheKey).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<RepoContext>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis repo-context cache read failed for key {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task SetAsync(
        string cacheKey,
        RepoContext context,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(context, JsonOptions);
            await db.StringSetAsync(cacheKey, json, ttl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis repo-context cache write failed for key {CacheKey}", cacheKey);
        }
    }
}
