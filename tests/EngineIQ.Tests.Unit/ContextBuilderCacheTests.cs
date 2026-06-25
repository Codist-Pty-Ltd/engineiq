using EngineIQ.ContextBuilder;
using EngineIQ.ContextBuilder.Architecture;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class ContextBuilderCacheTests
{
    [Fact]
    public async Task GetOrBuildAsync_uses_cache_on_second_call_when_pr_not_structural()
    {
        var tenantId = Guid.NewGuid();
        var cache = new InMemoryRepoContextCache();
        var github = new CountingGitHubClient();
        var service = new ContextBuilderService(
            github,
            cache,
            Options.Create(new RedisContextOptions { ContextCacheTtlHours = 24 }),
            NullLogger<ContextBuilderService>.Instance);

        var paths = new[] { "src/Acme.Domain/Foo.cs" };

        var first = await service.GetOrBuildAsync(tenantId, 1, "acme", "shop", paths);
        var second = await service.GetOrBuildAsync(tenantId, 1, "acme", "shop", paths);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.DetectedStyle, second!.DetectedStyle);
        Assert.Equal(1, github.TreeFetchCount);
        Assert.Equal(1, cache.SetCount);
        Assert.Equal(1, cache.HitCount);
    }

    [Fact]
    public async Task GetOrBuildAsync_reindexes_when_pr_touches_csproj()
    {
        var tenantId = Guid.NewGuid();
        var cache = new InMemoryRepoContextCache();
        var github = new CountingGitHubClient();
        var service = new ContextBuilderService(
            github,
            cache,
            Options.Create(new RedisContextOptions()),
            NullLogger<ContextBuilderService>.Instance);

        var paths = new[] { "src/Acme.Domain/Foo.cs" };
        await service.GetOrBuildAsync(tenantId, 1, "acme", "shop", paths);
        await service.GetOrBuildAsync(tenantId, 1, "acme", "shop", ["Acme.sln", "src/Acme.Domain/Foo.cs"]);

        Assert.Equal(2, github.TreeFetchCount);
        Assert.Equal(2, cache.SetCount);
    }

    [Fact]
    public void StructuralChangeDetector_flags_solution_and_project_files()
    {
        Assert.True(StructuralChangeDetector.TouchesStructuralFiles(["src/Foo.csproj"]));
        Assert.False(StructuralChangeDetector.TouchesStructuralFiles(["src/Foo.cs"]));
    }

    private sealed class InMemoryRepoContextCache : IRepoContextCache
    {
        private readonly Dictionary<string, RepoContext> _store = new(StringComparer.Ordinal);

        public int SetCount { get; private set; }
        public int HitCount { get; private set; }

        public string BuildCacheKey(Guid tenantId, string owner, string repo) =>
            $"engineiq:repo-context:{tenantId}:{owner}/{repo}";

        public Task<RepoContext?> TryGetAsync(string cacheKey, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(cacheKey, out var value))
            {
                HitCount++;
                return Task.FromResult<RepoContext?>(value);
            }

            return Task.FromResult<RepoContext?>(null);
        }

        public Task SetAsync(string cacheKey, RepoContext context, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            SetCount++;
            _store[cacheKey] = context;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingGitHubClient : IGitHubClient
    {
        public int TreeFetchCount { get; private set; }

        public Task<IReadOnlyList<string>> GetRepositoryFilePathsAsync(
            long installationId,
            string owner,
            string repo,
            CancellationToken cancellationToken = default)
        {
            TreeFetchCount++;
            IReadOnlyList<string> paths =
            [
                "src/Acme.Domain/Entities/Order.cs",
                "src/Acme.Application/Handlers/CreateOrder.cs",
                "src/Acme.Infrastructure/DbContext.cs",
                "src/Acme.API/Controllers/OrdersController.cs",
            ];
            return Task.FromResult(paths);
        }

        public Task<string> GetPullRequestDiffAsync(
            long installationId,
            string owner,
            string repo,
            int prNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<GitHubPullRequestInfo> GetPullRequestInfoAsync(
            long installationId,
            string owner,
            string repo,
            int prNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubPullRequestInfo(false, "test"));

        public Task PostReviewCommentAsync(
            long installationId,
            string owner,
            string repo,
            int prNumber,
            string body,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
