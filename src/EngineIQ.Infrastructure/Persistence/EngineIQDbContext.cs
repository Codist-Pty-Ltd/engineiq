using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class EngineIQDbContext : DbContext
{
    public EngineIQDbContext(DbContextOptions<EngineIQDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<PrReviewJob> PrReviewJobs => Set<PrReviewJob>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<TenantMetric> TenantMetrics => Set<TenantMetric>();
    public DbSet<PaystackWebhookEvent> PaystackWebhookEvents => Set<PaystackWebhookEvent>();

    public Task SetCurrentTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            return Task.CompletedTask;

        // set_config's second argument must be text; passing a Guid parameter becomes uuid and fails (42883).
        var tenantIdText = tenantId.ToString("D");
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantIdText}, true)",
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GitHubAppInstallationId).IsUnique();
            e.HasIndex(x => x.GitHubInstallState).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.Plan).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.GitHubOrgLogin).HasMaxLength(256);
            e.Property(x => x.GitHubInstallState).HasMaxLength(128);
            e.Property(x => x.ContactEmail).HasMaxLength(320);
            e.Property(x => x.ConfigYaml).HasColumnType("text");
            e.Property(x => x.FeatureFlagsJson).HasColumnType("jsonb");
            e.Property(x => x.PortalPreferencesJson).HasColumnType("jsonb");
            e.Property(x => x.DpaAcceptedIp).HasMaxLength(64);
            e.Property(x => x.PaystackCustomerCode).HasMaxLength(128);
            e.Property(x => x.PaystackSubscriptionCode).HasMaxLength(128);
            e.Property(x => x.BillingStatus).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Repository>(e =>
        {
            e.ToTable("repositories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.FullName }).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(512).IsRequired();
            e.Property(x => x.ArchitectureStyle).HasMaxLength(128);
            e.HasOne(x => x.Tenant).WithMany(x => x.Repositories).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrReviewJob>(e =>
        {
            e.ToTable("pr_review_jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.GithubDeliveryId }).IsUnique();
            e.Property(x => x.GithubDeliveryId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.EstimatedCostZar).HasPrecision(18, 6);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Repository).WithMany(r => r.Jobs).HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Finding>(e =>
        {
            e.ToTable("findings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.JobId });
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            e.Property(x => x.Category).HasMaxLength(128).IsRequired();
            e.Property(x => x.RuleId).HasMaxLength(128);
            e.Property(x => x.Source).HasMaxLength(16).IsRequired();
            e.Property(x => x.FilePath).HasMaxLength(2048).IsRequired();
            e.Property(x => x.Message).HasMaxLength(8192).IsRequired();
            e.Property(x => x.PrMergeStatus).HasMaxLength(64).IsRequired();
            e.Property(x => x.TrainingFeaturesJson).HasColumnType("jsonb");
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Job).WithMany(x => x.Findings).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantMetric>(e =>
        {
            e.ToTable("tenant_metrics");
            e.HasKey(x => new { x.TenantId, x.Date });
            e.Property(x => x.TokenCostZar).HasPrecision(18, 6);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaystackWebhookEvent>(e =>
        {
            e.ToTable("paystack_webhook_events");
            e.HasKey(x => x.EventKey);
            e.Property(x => x.EventKey).HasMaxLength(256);
            e.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Tenant>().HasData(
            new Tenant
            {
                Id = WellKnownTenants.BillableId,
                Name = "Billable",
                Plan = "Growth",
                GitHubOrgId = null,
                GitHubOrgLogin = null,
                GitHubAppInstallationId = 9_000_000_000_001,
                WebhookSecretHash = null,
                ApiKeyHash = null,
                GitHubInstallState = null,
                ContactEmail = null,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Status = "Active",
                ConfigYaml = "# Billable default — replace github_app_installation_id when GitHub App is installed for this org.",
                FeatureFlagsJson = null,
                BillingStatus = "Internal",
            });
    }
}
