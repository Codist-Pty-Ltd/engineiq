using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Tenants;
using EngineIQ.Infrastructure.Paystack;
using EngineIQ.Infrastructure.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class TenantBillingTests : IDisposable
{
    private readonly SqliteTestDatabase _db;
    private readonly TenantRepository _tenants;
    private readonly RecordingPaystackClient _paystack;
    private readonly TenantBillingService _billing;

    public TenantBillingTests()
    {
        _db = SqliteTestDatabase.Create();
        _tenants = new TenantRepository(_db.Factory, NullLogger<TenantRepository>.Instance);
        _paystack = new RecordingPaystackClient();
        _billing = new TenantBillingService(
            _tenants,
            _paystack,
            Options.Create(new PaystackOptions
            {
                SecretKey = "sk_test",
                PlanStarter = "PLN_starter",
                PlanGrowth = "PLN_growth",
                PlanScale = "PLN_scale",
            }),
            NullLogger<TenantBillingService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RegisterAsync_sets_Trialing_and_30_day_trial_for_standard_email()
    {
        var result = await _tenants.RegisterAsync(new RegisterTenantCommand(
            "newco@example.com",
            "New Co",
            PlanCatalog.Starter,
            "newco",
            "127.0.0.1"));

        var billing = await _tenants.GetBillingRowAsync(result.TenantId);
        Assert.NotNull(billing);
        Assert.Equal(BillingStatuses.Trialing, billing!.BillingStatus);
        Assert.NotNull(billing.TrialEndsAt);
        Assert.True(billing.TrialEndsAt > DateTimeOffset.UtcNow.AddDays(29));
        Assert.Null(billing.PaystackCustomerCode);
    }

    [Fact]
    public async Task RegisterAsync_sets_Internal_for_golden_four_email()
    {
        var result = await _tenants.RegisterAsync(new RegisterTenantCommand(
            "hello@mybillable.co.za",
            "Mybillable",
            PlanCatalog.Growth,
            "mybillable",
            "127.0.0.1"));

        var billing = await _tenants.GetBillingRowAsync(result.TenantId);
        Assert.NotNull(billing);
        Assert.Equal(BillingStatuses.Internal, billing!.BillingStatus);
        Assert.Null(billing.TrialEndsAt);
    }

    [Fact]
    public async Task ProvisionCustomerAfterRegister_creates_paystack_customer_for_trialing_tenant()
    {
        var result = await _tenants.RegisterAsync(new RegisterTenantCommand(
            "billing@example.com",
            "Billing Co",
            PlanCatalog.Starter,
            "billingco",
            "127.0.0.1"));

        await _billing.ProvisionCustomerAfterRegisterAsync(
            result.TenantId,
            "billing@example.com",
            "Billing Co");

        Assert.Equal(1, _paystack.CreateCustomerCallCount);
        var billing = await _tenants.GetBillingRowAsync(result.TenantId);
        Assert.Equal("CUS_test_1", billing!.PaystackCustomerCode);
    }

    [Fact]
    public async Task ProvisionCustomerAfterRegister_skips_paystack_for_internal_tenant()
    {
        var result = await _tenants.RegisterAsync(new RegisterTenantCommand(
            "technical@codist.co.za",
            "War Room",
            PlanCatalog.Growth,
            "warroom",
            "127.0.0.1"));

        await _billing.ProvisionCustomerAfterRegisterAsync(
            result.TenantId,
            "technical@codist.co.za",
            "War Room");

        Assert.Equal(0, _paystack.CreateCustomerCallCount);
        var billing = await _tenants.GetBillingRowAsync(result.TenantId);
        Assert.Null(billing!.PaystackCustomerCode);
        Assert.Equal(BillingStatuses.Internal, billing.BillingStatus);
    }

    [Fact]
    public async Task StartSubscriptionCheckout_returns_authorization_url()
    {
        var tenantId = await SeedTrialingTenantAsync("checkout@example.com");

        var checkout = await _billing.StartSubscriptionCheckoutAsync(
            tenantId,
            PlanCatalog.Growth,
            "https://app.engineiq.co.za/billing/callback");

        Assert.Equal("https://checkout.paystack.test/abc", checkout.AuthorizationUrl);
        Assert.Equal("ref_123", checkout.Reference);
    }

    private async Task<Guid> SeedTrialingTenantAsync(string email)
    {
        var result = await _tenants.RegisterAsync(new RegisterTenantCommand(
            email,
            "Checkout Co",
            PlanCatalog.Starter,
            "checkoutco",
            "127.0.0.1"));
        await _billing.ProvisionCustomerAfterRegisterAsync(result.TenantId, email, "Checkout Co");
        return result.TenantId;
    }

    private sealed class RecordingPaystackClient : IPaystackClient
    {
        public int CreateCustomerCallCount { get; private set; }

        public bool IsConfigured => true;

        public Task<string> CreateCustomerAsync(string email, string firstName, CancellationToken cancellationToken = default)
        {
            CreateCustomerCallCount++;
            return Task.FromResult("CUS_test_1");
        }

        public Task<PaystackInitializeResult> InitializeSubscriptionCheckoutAsync(
            string email,
            string paystackPlanCode,
            string callbackUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaystackInitializeResult("ref_123", "https://checkout.paystack.test/abc"));

        public Task<PaystackVerifyTransactionResult> VerifyTransactionAsync(
            string reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaystackVerifyTransactionResult(true, "SUB_1", "CUS_test_1", "PLN_growth"));

        public Task<string> UpdateSubscriptionPlanAsync(
            string subscriptionCode,
            string paystackPlanCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(subscriptionCode);
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
