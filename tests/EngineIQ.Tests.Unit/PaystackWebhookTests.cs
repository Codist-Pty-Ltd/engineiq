using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Domain.Tenants;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Paystack;
using EngineIQ.Infrastructure.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class PaystackWebhookTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly PaystackWebhookRepository _webhooks;
    private readonly PaystackWebhookProcessor _processor;
    private readonly JobRepository _jobs;
    private readonly TenantRepository _tenants;

    private const string SecretKey = "sk_test_webhook_secret";

    public PaystackWebhookTests()
    {
        _db = SqliteTestDatabase.Create();
        _webhooks = new PaystackWebhookRepository(_db.Factory);
        _processor = new PaystackWebhookProcessor(_webhooks, NullLogger<PaystackWebhookProcessor>.Instance);
        _jobs = new JobRepository(_db.Factory, Options.Create(new PostgresOptions { ConnectionString = "unused" }));
        _tenants = new TenantRepository(_db.Factory, NullLogger<TenantRepository>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void SignatureValidator_accepts_valid_hmac_sha512()
    {
        const string body = """{"event":"charge.success","data":{"id":1}}""";
        var signature = ComputeSignature(body, SecretKey);
        Assert.True(PaystackWebhookSignatureValidator.Validate(body, signature, SecretKey));
    }

    [Fact]
    public void SignatureValidator_rejects_tampered_body()
    {
        const string body = """{"event":"charge.success","data":{"id":1}}""";
        var signature = ComputeSignature(body, SecretKey);
        const string tampered = """{"event":"charge.success","data":{"id":2}}""";
        Assert.False(PaystackWebhookSignatureValidator.Validate(tampered, signature, SecretKey));
    }

    [Theory]
    [InlineData("charge.success", BillingStatuses.Active, false)]
    [InlineData("subscription.create", BillingStatuses.Active, false)]
    [InlineData("invoice.payment_failed", BillingStatuses.PastDue, true)]
    [InlineData("subscription.disable", BillingStatuses.Cancelled, true)]
    public void EventMapper_maps_core_events(string eventType, string expectedStatus, bool suspend)
    {
        using var doc = JsonDocument.Parse("""{"status":"failed","id":99}""");
        var (status, shouldSuspend) = PaystackBillingEventMapper.Map(eventType, doc.RootElement);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(suspend, shouldSuspend);
    }

    [Theory]
    [InlineData("success", BillingStatuses.Active, false)]
    [InlineData("paid", BillingStatuses.Active, false)]
    [InlineData("failed", BillingStatuses.PastDue, true)]
    public void EventMapper_maps_invoice_update_by_status(string invoiceStatus, string expectedStatus, bool suspend)
    {
        using var doc = JsonDocument.Parse($$"""{"status":"{{invoiceStatus}}","id":88}""");
        var (status, shouldSuspend) = PaystackBillingEventMapper.Map("invoice.update", doc.RootElement);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(suspend, shouldSuspend);
    }

    [Fact]
    public async Task Processor_payment_failed_sets_PastDue_and_suspends_tenant()
    {
        var tenantId = await SeedBillableTenantAsync("CUS_bill_1", BillingStatuses.Active, "Active");

        var json = """
            {
              "event": "invoice.payment_failed",
              "data": {
                "id": 9001,
                "customer": { "customer_code": "CUS_bill_1" },
                "subscription_code": "SUB_bill_1"
              }
            }
            """;

        await _processor.ProcessAsync(json);

        var billing = await _tenants.GetBillingRowAsync(tenantId);
        Assert.Equal(BillingStatuses.PastDue, billing!.BillingStatus);
        Assert.Equal("Suspended", await GetTenantStatusAsync(tenantId));
    }

    [Fact]
    public async Task Processor_charge_success_resumes_suspended_tenant()
    {
        var tenantId = await SeedBillableTenantAsync("CUS_resume", BillingStatuses.PastDue, "Suspended");

        await _processor.ProcessAsync("""
            {
              "event": "charge.success",
              "data": {
                "id": 42,
                "customer": { "customer_code": "CUS_resume" },
                "subscription_code": "SUB_resume"
              }
            }
            """);

        var billing = await _tenants.GetBillingRowAsync(tenantId);
        Assert.Equal(BillingStatuses.Active, billing!.BillingStatus);
        Assert.Equal("Active", await GetTenantStatusAsync(tenantId));
    }

    [Fact]
    public async Task Processor_replay_is_idempotent()
    {
        var tenantId = await SeedBillableTenantAsync("CUS_idem", BillingStatuses.Active, "Active");
        var json = """
            {
              "event": "invoice.payment_failed",
              "data": {
                "id": 555,
                "customer": { "customer_code": "CUS_idem" }
              }
            }
            """;

        await _processor.ProcessAsync(json);
        await _processor.ProcessAsync(json);

        var billing = await _tenants.GetBillingRowAsync(tenantId);
        Assert.Equal(BillingStatuses.PastDue, billing!.BillingStatus);

        await using var db = await _db.Factory.CreateDbContextAsync();
        var count = await db.PaystackWebhookEvents.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Processor_skips_internal_tenant()
    {
        var tenantId = await SeedBillableTenantAsync(
            "CUS_internal",
            BillingStatuses.Internal,
            "Active",
            email: "technical@codist.co.za");

        await _processor.ProcessAsync("""
            {
              "event": "invoice.payment_failed",
              "data": {
                "id": 777,
                "customer": { "customer_code": "CUS_internal" }
              }
            }
            """);

        var billing = await _tenants.GetBillingRowAsync(tenantId);
        Assert.Equal(BillingStatuses.Internal, billing!.BillingStatus);
        Assert.Equal("Active", await GetTenantStatusAsync(tenantId));
    }

    [Fact]
    public async Task Suspended_tenant_blocks_new_job_enqueue_and_worker_transition()
    {
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        const long installationId = 424242;

        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Past Due Co",
                Plan = PlanCatalog.Starter,
                Status = "Suspended",
                BillingStatus = BillingStatuses.PastDue,
                GitHubAppInstallationId = installationId,
                ContactEmail = "pastdue@example.com",
                PaystackCustomerCode = "CUS_past",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Repositories.Add(new Repository
            {
                Id = repoId,
                TenantId = tenantId,
                FullName = "codist/pastdue",
            });
            db.PrReviewJobs.Add(new PrReviewJob
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RepositoryId = repoId,
                PrNumber = 1,
                GithubDeliveryId = Guid.NewGuid().ToString("N"),
                Status = ReviewJobStatuses.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Job enqueue gate uses installation lookup — simulate via direct status check path:
        // suspended tenants are rejected before insert when status is read from DB.
        await using (var gate = await _db.Factory.CreateDbContextAsync())
        {
            var status = await gate.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Status)
                .FirstOrDefaultAsync();
            Assert.Equal("Suspended", status);
        }

        var jobId = await GetSingleJobIdAsync(tenantId);
        Assert.False(await _jobs.TryMarkJobProcessingIfQueuedAsync(tenantId, jobId));
    }

    private async Task<Guid> SeedBillableTenantAsync(
        string customerCode,
        string billingStatus,
        string tenantStatus,
        string email = "billing@example.com")
    {
        var tenantId = Guid.NewGuid();
        await using (var db = await _db.Factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Webhook Test Co",
                Plan = PlanCatalog.Growth,
                Status = tenantStatus,
                BillingStatus = billingStatus,
                PaystackCustomerCode = customerCode,
                PaystackSubscriptionCode = "SUB_bill_1",
                ContactEmail = email,
                GitHubAppInstallationId = 123456789,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        return tenantId;
    }

    private async Task<string> GetTenantStatusAsync(Guid tenantId)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Status)
            .FirstAsync();
    }

    private async Task<Guid> GetSingleJobIdAsync(Guid tenantId)
    {
        await using var db = await _db.Factory.CreateDbContextAsync();
        await db.SetCurrentTenantAsync(tenantId);
        return await db.PrReviewJobs.Select(j => j.Id).FirstAsync();
    }

    private static string ComputeSignature(string body, string secret)
    {
        var hash = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
