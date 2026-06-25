using EngineIQ.Domain.Security;
using EngineIQ.Infrastructure.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIQ.Tests.Unit;

public class TenantApiKeyRotationTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly TenantRepository _repository;

    public TenantApiKeyRotationTests()
    {
        _db = SqliteTestDatabase.Create();
        _repository = new TenantRepository(_db.Factory, NullLogger<TenantRepository>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RotateApiKeyAsync_invalidates_old_key_and_accepts_new_key()
    {
        var tenantId = Guid.NewGuid();
        var oldKey = TenantApiKeyMaterial.Generate(tenantId);

        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Rotation Test Co",
                Plan = "Starter",
                Status = "Active",
                ApiKeyHash = TenantApiKeyMaterial.Hash(oldKey),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(tenantId, await _repository.ValidateApiKeyAndGetTenantIdAsync(oldKey));

        var (ok, newKey) = await _repository.RotateApiKeyAsync(tenantId);
        Assert.True(ok);
        Assert.NotNull(newKey);
        Assert.NotEqual(oldKey, newKey);
        Assert.StartsWith($"{tenantId:N}.", newKey, StringComparison.Ordinal);

        Assert.Null(await _repository.ValidateApiKeyAndGetTenantIdAsync(oldKey));
        Assert.Equal(tenantId, await _repository.ValidateApiKeyAndGetTenantIdAsync(newKey!));
    }

    [Fact]
    public async Task RotateApiKeyAsync_returns_false_for_unknown_tenant()
    {
        var (ok, apiKey) = await _repository.RotateApiKeyAsync(Guid.NewGuid());
        Assert.False(ok);
        Assert.Null(apiKey);
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
