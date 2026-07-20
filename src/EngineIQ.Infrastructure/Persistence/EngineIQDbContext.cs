using EngineIQ.Domain.Persistence;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class EngineIQDbContext : DbContext, IDataProtectionKeyContext
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
    public DbSet<JiraConnection> JiraConnections => Set<JiraConnection>();
    public DbSet<IssueAnalysisJob> IssueAnalysisJobs => Set<IssueAnalysisJob>();
    public DbSet<AnalyzedIssue> AnalyzedIssues => Set<AnalyzedIssue>();
    public DbSet<BacklogBackfill> BacklogBackfills => Set<BacklogBackfill>();
    public DbSet<CodeChunk> CodeChunks => Set<CodeChunk>();
    public DbSet<RepoIndexJob> RepoIndexJobs => Set<RepoIndexJob>();
    public DbSet<JiraProjectRepoMapping> JiraProjectRepoMappings => Set<JiraProjectRepoMapping>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Vector column dimensions (Voyage voyage-code-3 default); Session13 code index.</summary>
    public const int EmbeddingDimensions = 1024;

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
        if (Database.IsNpgsql())
            modelBuilder.HasPostgresExtension("vector");

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
            e.Property(x => x.IndexedCommitSha).HasMaxLength(64);
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
            e.Property(x => x.IssuesAnalyzed);
            e.Property(x => x.ChunksEmbedded);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaystackWebhookEvent>(e =>
        {
            e.ToTable("paystack_webhook_events");
            e.HasKey(x => x.EventKey);
            e.Property(x => x.EventKey).HasMaxLength(256);
            e.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<JiraConnection>(e =>
        {
            e.ToTable("jira_connections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.WebhookSecret).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.SiteBaseUrl).HasMaxLength(512).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.ApiTokenProtected).HasColumnType("text").IsRequired();
            e.Property(x => x.WebhookSecret).HasMaxLength(128).IsRequired();
            e.Property(x => x.ProjectKeysCsv).HasMaxLength(1024);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IssueAnalysisJob>(e =>
        {
            e.ToTable("issue_analysis_jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.DedupeKey }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.Property(x => x.IssueKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.DedupeKey).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(512);
            e.Property(x => x.Trigger).HasMaxLength(32);
            e.Property(x => x.EstimatedCostZar).HasPrecision(18, 6);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JiraConnection).WithMany().HasForeignKey(x => x.JiraConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyzedIssue>(e =>
        {
            e.ToTable("analyzed_issues");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JiraConnectionId, x.JiraIssueId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.JiraConnectionId });
            e.Property(x => x.IssueKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.JiraCommentId).HasMaxLength(128).IsRequired();
            e.Property(x => x.LastTrigger).HasMaxLength(32).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JiraConnection).WithMany().HasForeignKey(x => x.JiraConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BacklogBackfill>(e =>
        {
            e.ToTable("backlog_backfills");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.JiraConnectionId, x.Status });
            e.Property(x => x.Jql).HasColumnType("text").IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(512);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JiraConnection).WithMany().HasForeignKey(x => x.JiraConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CodeChunk>(e =>
        {
            e.ToTable("code_chunks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RepositoryId, x.FilePath, x.ContentSha256 }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RepositoryId });
            e.Property(x => x.FilePath).HasMaxLength(2048).IsRequired();
            e.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.SymbolName).HasMaxLength(512);
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.Content).HasColumnType("text").IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Repository).WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Cascade);

            // pgvector's "vector" column type has no Sqlite equivalent; the unit-test Sqlite provider
            // never persists CodeChunk rows, so the embedding column is simply excluded from that model.
            if (Database.IsNpgsql())
                e.Property(x => x.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            else
                e.Ignore(x => x.Embedding);
        });

        modelBuilder.Entity<RepoIndexJob>(e =>
        {
            e.ToTable("repo_index_jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.DedupeKey }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.RepositoryId, x.Status });
            e.Property(x => x.Owner).HasMaxLength(256).IsRequired();
            e.Property(x => x.Repo).HasMaxLength(256).IsRequired();
            e.Property(x => x.HeadSha).HasMaxLength(64).IsRequired();
            e.Property(x => x.BaseSha).HasMaxLength(64);
            e.Property(x => x.DedupeKey).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(512);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Repository).WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JiraProjectRepoMapping>(e =>
        {
            e.ToTable("jira_project_repo_mappings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JiraConnectionId, x.ProjectKey, x.RepositoryId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.JiraConnectionId });
            e.Property(x => x.ProjectKey).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JiraConnection).WithMany().HasForeignKey(x => x.JiraConnectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Repository).WithMany().HasForeignKey(x => x.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataProtectionKey>(e =>
        {
            e.ToTable("data_protection_keys");
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
