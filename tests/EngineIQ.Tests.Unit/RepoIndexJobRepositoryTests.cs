using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class RepoIndexJobRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly RepoIndexJobRepository _jobs;

    public RepoIndexJobRepositoryTests()
    {
        _db = SqliteTestDatabase.Create();
        _jobs = new RepoIndexJobRepository(
            _db.Factory,
            Options.Create(new PostgresOptions { ConnectionString = "unused" }));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task TryCreateQueuedJobAsync_creates_job_in_PendingPublish()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();

        var result = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, installationId: 42, owner: "codist", repo: "engineiq",
            headSha: "abc123", baseSha: null, dedupeKey: "push:delivery-1");

        Assert.True(result.Created);
        Assert.NotNull(result.JobId);

        var job = await GetJobAsync(tenantId, result.JobId!.Value);
        Assert.Equal(ReviewJobStatuses.PendingPublish, job!.Status);
        Assert.Equal("abc123", job.HeadSha);
    }

    [Fact]
    public async Task TryCreateQueuedJobAsync_duplicate_dedupe_key_still_PendingPublish_needs_republish()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();
        var dedupeKey = "push:delivery-2";

        var first = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, dedupeKey);
        Assert.True(first.Created);

        var second = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, dedupeKey);

        Assert.False(second.Created);
        Assert.True(second.NeedsRepublish);
        Assert.Equal(first.JobId, second.JobId);
    }

    [Fact]
    public async Task TryCreateQueuedJobAsync_duplicate_after_queued_is_plain_duplicate()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();
        var dedupeKey = "push:delivery-3";

        var first = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, dedupeKey);
        await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, first.JobId!.Value);

        var second = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, dedupeKey);

        Assert.False(second.Created);
        Assert.False(second.NeedsRepublish);
        Assert.Equal("duplicate", second.BlockReason);
        Assert.Equal(first.JobId, second.JobId);
    }

    [Fact]
    public async Task TryFindActiveJobForRepoAsync_true_while_pending_false_after_completion()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();
        var created = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, "push:delivery-4");

        Assert.Equal(created.JobId, await _jobs.FindActiveJobIdForRepoAsync(tenantId, repositoryId));

        await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, created.JobId!.Value);
        Assert.True(await _jobs.TryMarkJobProcessingIfQueuedAsync(tenantId, created.JobId!.Value));
        Assert.Equal(created.JobId, await _jobs.FindActiveJobIdForRepoAsync(tenantId, repositoryId));

        await _jobs.MarkJobCompletedAsync(tenantId, created.JobId!.Value, 1234, 10, 20, 5, 1);
        Assert.Null(await _jobs.FindActiveJobIdForRepoAsync(tenantId, repositoryId));
    }

    [Fact]
    public async Task MarkJobCompletedAsync_persists_stats()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();
        var created = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, "push:delivery-5");

        await _jobs.MarkJobCompletedAsync(tenantId, created.JobId!.Value, 5000, filesWalked: 12, chunksTotal: 40, chunksEmbedded: 8, chunksDeleted: 2);

        var job = await GetJobAsync(tenantId, created.JobId!.Value);
        Assert.Equal(ReviewJobStatuses.Completed, job!.Status);
        Assert.Equal(12, job.FilesWalked);
        Assert.Equal(40, job.ChunksTotal);
        Assert.Equal(8, job.ChunksEmbedded);
        Assert.Equal(2, job.ChunksDeleted);
        Assert.Equal(5000, job.DurationMs);
    }

    [Fact]
    public async Task MarkJobFailedAsync_persists_failure_reason()
    {
        var (tenantId, repositoryId) = await SeedTenantAndRepositoryAsync();
        var created = await _jobs.TryCreateQueuedJobAsync(
            tenantId, repositoryId, 42, "codist", "engineiq", "sha1", null, "push:delivery-6");

        await _jobs.MarkJobFailedAsync(tenantId, created.JobId!.Value, "boom", 999);

        var job = await GetJobAsync(tenantId, created.JobId!.Value);
        Assert.Equal(ReviewJobStatuses.Failed, job!.Status);
        Assert.Equal("boom", job.FailureReason);
    }

    private async Task<(Guid TenantId, Guid RepositoryId)> SeedTenantAndRepositoryAsync()
    {
        var tenantId = Guid.NewGuid();
        var repositoryId = Guid.NewGuid();

        await using var db = await _db.Factory.CreateDbContextAsync();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Code Index Co",
            Plan = "Starter",
            Status = "Active",
            GitHubAppInstallationId = 42,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.Repositories.Add(new Repository
        {
            Id = repositoryId,
            TenantId = tenantId,
            FullName = "codist/engineiq"
        });
        await db.SaveChangesAsync();

        return (tenantId, repositoryId);
    }

    private async Task<RepoIndexJob?> GetJobAsync(Guid tenantId, Guid jobId)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        await db.SetCurrentTenantAsync(tenantId);
        return await db.RepoIndexJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
    }

    private sealed class SqliteTestDatabase : IDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

        private SqliteTestDatabase(Microsoft.Data.Sqlite.SqliteConnection connection, IDbContextFactory<EngineIQDbContext> factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public IDbContextFactory<EngineIQDbContext> Factory { get; }

        public static SqliteTestDatabase Create()
        {
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<EngineIQDbContext>()
                .UseSqlite(connection)
                .Options;
            using (var db = new EngineIQDbContext(options))
            {
                db.Database.EnsureCreated();
            }

            return new SqliteTestDatabase(connection, new SqliteDbContextFactory(options));
        }

        public void Dispose() => _connection.Dispose();

        private sealed class SqliteDbContextFactory : IDbContextFactory<EngineIQDbContext>
        {
            private readonly DbContextOptions<EngineIQDbContext> _options;

            public SqliteDbContextFactory(DbContextOptions<EngineIQDbContext> options) => _options = options;

            public EngineIQDbContext CreateDbContext() => new(_options);

            public ValueTask<EngineIQDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(CreateDbContext());
        }
    }
}
