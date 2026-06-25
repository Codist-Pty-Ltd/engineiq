using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class PendingPublishRecoveryTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly JobRepository _jobs;

    public PendingPublishRecoveryTests()
    {
        _db = SqliteTestDatabase.Create();
        _jobs = new JobRepository(
            _db.Factory,
            Options.Create(new PostgresOptions { ConnectionString = "unused" }));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Publish_failure_leaves_job_in_PendingPublish_for_reconciler()
    {
        var (tenantId, jobId) = await SeedPendingPublishJobAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        var stale = await _jobs.ListStalePendingPublishJobsAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(stale, j => j.JobId == jobId && j.TenantId == tenantId);

        var job = await GetJobAsync(tenantId, jobId);
        Assert.Equal(ReviewJobStatuses.PendingPublish, job!.Status);
    }

    [Fact]
    public async Task MarkJobQueuedAfterPublish_moves_job_to_Queued()
    {
        var (tenantId, jobId) = await SeedPendingPublishJobAsync();

        Assert.True(await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, jobId));

        var job = await GetJobAsync(tenantId, jobId);
        Assert.Equal(ReviewJobStatuses.Queued, job!.Status);
    }

    [Fact]
    public async Task MarkJobQueuedAfterPublish_does_not_change_non_pending_job()
    {
        var (tenantId, jobId) = await SeedPendingPublishJobAsync();
        Assert.True(await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, jobId));
        Assert.False(await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, jobId));
    }

    [Fact]
    public async Task TryMarkJobProcessingIfQueued_accepts_PendingPublish_and_Queued_only_once()
    {
        var (tenantId, jobId) = await SeedPendingPublishJobAsync();

        Assert.True(await _jobs.TryMarkJobProcessingIfQueuedAsync(tenantId, jobId));
        Assert.False(await _jobs.TryMarkJobProcessingIfQueuedAsync(tenantId, jobId));

        var job = await GetJobAsync(tenantId, jobId);
        Assert.Equal(ReviewJobStatuses.Processing, job!.Status);
    }

    [Fact]
    public async Task Reconciler_recovery_path_publish_then_mark_does_not_lose_job()
    {
        var (tenantId, jobId) = await SeedPendingPublishJobAsync(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        // Simulate successful reconciler publish after webhook publish failure.
        Assert.True(await _jobs.MarkJobQueuedAfterPublishAsync(tenantId, jobId));

        var job = await GetJobAsync(tenantId, jobId);
        Assert.NotNull(job);
        Assert.Equal(ReviewJobStatuses.Queued, job.Status);
        Assert.NotEqual(default, job.GithubDeliveryId);
    }

    private async Task<(Guid TenantId, Guid JobId)> SeedPendingPublishJobAsync(
        DateTimeOffset? createdAt = null)
    {
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var at = createdAt ?? DateTimeOffset.UtcNow;

        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Pending Publish Co",
                Plan = "Starter",
                Status = "Active",
                GitHubAppInstallationId = 42,
                CreatedAt = at
            });
            db.Repositories.Add(new Repository
            {
                Id = repoId,
                TenantId = tenantId,
                FullName = "codist/engineiq"
            });
            db.PrReviewJobs.Add(new PrReviewJob
            {
                Id = jobId,
                TenantId = tenantId,
                RepositoryId = repoId,
                PrNumber = 7,
                GithubDeliveryId = Guid.NewGuid().ToString("N"),
                Status = ReviewJobStatuses.PendingPublish,
                CreatedAt = at
            });
            await db.SaveChangesAsync();
        }

        return (tenantId, jobId);
    }

    private async Task<PrReviewJob?> GetJobAsync(Guid tenantId, Guid jobId)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        await db.SetCurrentTenantAsync(tenantId);
        return await db.PrReviewJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
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
