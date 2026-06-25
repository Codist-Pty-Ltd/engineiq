using EngineIQ.ContextBuilder.Architecture;
using EngineIQ.ContextBuilder.Parsing;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ContextBuilder;

public sealed class ContextBuilderService : IContextBuilder
{
    private readonly IGitHubClient _gitHubClient;
    private readonly IRepoContextCache _cache;
    private readonly RedisContextOptions _options;
    private readonly ILogger<ContextBuilderService> _logger;

    public ContextBuilderService(
        IGitHubClient gitHubClient,
        IRepoContextCache cache,
        IOptions<RedisContextOptions> options,
        ILogger<ContextBuilderService> logger)
    {
        _gitHubClient = gitHubClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RepoContext?> GetOrBuildAsync(
        Guid tenantId,
        long installationId,
        string owner,
        string repo,
        IReadOnlyList<string> prFilePaths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = _cache.BuildCacheKey(tenantId, owner, repo);
            var cached = await _cache.TryGetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null && !StructuralChangeDetector.TouchesStructuralFiles(prFilePaths))
            {
                _logger.LogDebug("Repo context cache hit for {Owner}/{Repo}", owner, repo);
                return cached;
            }

            _logger.LogInformation(
                "Building repo context for {Owner}/{Repo} (cache miss or structural PR change)",
                owner,
                repo);

            IReadOnlyList<string> paths;
            try
            {
                paths = await _gitHubClient
                    .GetRepositoryFilePathsAsync(installationId, owner, repo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GitHub tree fetch failed for {Owner}/{Repo}; using PR paths", owner, repo);
                paths = prFilePaths;
            }

            if (paths.Count == 0 && prFilePaths.Count > 0)
                paths = prFilePaths;

            if (paths.Count == 0)
                return null;

            var context = ArchitectureStyleDetector.Detect(paths);
            var ttl = TimeSpan.FromHours(Math.Max(1, _options.ContextCacheTtlHours));
            await _cache.SetAsync(cacheKey, context, ttl, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repo context build failed for {Owner}/{Repo}", owner, repo);
            return null;
        }
    }
}

/// <summary>Context-builder cache TTL (mirrors Redis options section).</summary>
public sealed class RedisContextOptions
{
    public const string SectionName = "Redis";

    public int ContextCacheTtlHours { get; set; } = 24;
}
