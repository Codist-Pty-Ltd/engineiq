using System.Security.Cryptography;
using System.Text;
using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Domain.Security;
using EngineIQ.Domain.Tenants;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class TenantRepository : ITenantRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly ILogger<TenantRepository> _logger;

    public TenantRepository(IDbContextFactory<EngineIQDbContext> factory, ILogger<TenantRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<RegisterTenantResult> RegisterAsync(RegisterTenantCommand command, CancellationToken cancellationToken = default)
    {
        var tenantId = Guid.NewGuid();
        var apiKey = TenantApiKeyMaterial.Generate(tenantId);
        var apiKeyHash = TenantApiKeyMaterial.Hash(apiKey);
        var webhookSecret = $"whsec_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var webhookSecretHash = SHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret));
        var dpaAt = DateTimeOffset.UtcNow;
        var dpaIp = TruncateDpaIp(command.DpaAcceptedIp);
        var isInternal = InternalTenantBilling.IsInternalEmail(command.Email);

        for (var i = 0; i < 5; i++)
        {
            var installState = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            var tenant = new Tenant
            {
                Id = tenantId,
                Name = command.CompanyName.Trim(),
                Plan = command.Plan.Trim(),
                GitHubOrgId = null,
                GitHubOrgLogin = command.GithubOrg.Trim(),
                GitHubAppInstallationId = null,
                WebhookSecretHash = webhookSecretHash,
                ApiKeyHash = apiKeyHash,
                GitHubInstallState = installState,
                ContactEmail = command.Email.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "AwaitingGitHubInstall",
                ConfigYaml = null,
                DpaAcceptedAt = dpaAt,
                DpaAcceptedIp = dpaIp,
                BillingStatus = isInternal ? BillingStatuses.Internal : BillingStatuses.Trialing,
                TrialEndsAt = isInternal ? null : dpaAt.AddDays(30),
            };

            db.Tenants.Add(tenant);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new RegisterTenantResult(tenantId, apiKey, installState, webhookSecret);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _logger.LogDebug(ex, "Retrying tenant register after unique collision on install state.");
            }
        }

        throw new InvalidOperationException("Could not allocate a unique GitHub install state.");
    }

    public async Task<(bool Ok, Guid? TenantId, string? ContactEmail, string? Error)> CompleteGitHubInstallAsync(
        long installationId,
        string installState,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installState))
            return (false, null, null, "missing_state");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(
            t => t.GitHubInstallState == installState.Trim(),
            cancellationToken);

        if (tenant is null)
            return (false, null, null, "unknown_state");

        var contactEmail = tenant.ContactEmail;

        if (tenant.GitHubAppInstallationId == installationId)
        {
            tenant.GitHubInstallState = null;
            tenant.Status = "Active";
            await db.SaveChangesAsync(cancellationToken);
            return (true, tenant.Id, contactEmail, null);
        }

        if (tenant.GitHubAppInstallationId is long existing && existing != installationId)
            return (false, null, null, "tenant_already_linked");

        var taken = await db.Tenants.AnyAsync(
            t => t.GitHubAppInstallationId == installationId && t.Id != tenant.Id,
            cancellationToken);
        if (taken)
            return (false, null, null, "installation_in_use");

        tenant.GitHubAppInstallationId = installationId;
        tenant.GitHubInstallState = null;
        tenant.Status = "Active";

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return (false, null, null, "installation_in_use");
        }

        return (true, tenant.Id, contactEmail, null);
    }

    public async Task<Guid?> ValidateApiKeyAndGetTenantIdAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (!TenantApiKeyMaterial.TryParseTenantId(apiKey, out var tenantId))
            return null;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId && t.ApiKeyHash != null)
            .Select(t => new { t.ApiKeyHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.ApiKeyHash is null)
            return null;

        return TenantApiKeyMaterial.FixedTimeEqualsHash(row.ApiKeyHash, apiKey) ? tenantId : null;
    }

    public async Task<(bool Ok, string? ApiKeyPlaintext)> RotateApiKeyAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return (false, null);

        var apiKey = TenantApiKeyMaterial.Generate(tenantId);
        tenant.ApiKeyHash = TenantApiKeyMaterial.Hash(apiKey);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Rotated API key for tenant {TenantId}.", tenantId);
        return (true, apiKey);
    }

    public async Task<TenantStatusSnapshot?> GetStatusSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.GitHubAppInstallationId })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
            return null;

        var repos = await db.Repositories.CountAsync(cancellationToken);
        var firstPr = await db.PrReviewJobs.AnyAsync(
            j => j.Status == ReviewJobStatuses.Completed,
            cancellationToken);

        var onboarding = tenant.GitHubAppInstallationId is null ? "pending_github_install" : "live";
        return new TenantStatusSnapshot(onboarding, repos, firstPr);
    }

    public async Task<(bool Ok, string? InstallState, string? Error)> EnsureGitHubInstallStateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return (false, null, "not_found");

        if (tenant.GitHubAppInstallationId.HasValue)
            return (false, null, "already_installed");

        if (!string.IsNullOrWhiteSpace(tenant.GitHubInstallState))
            return (true, tenant.GitHubInstallState.Trim(), null);

        for (var i = 0; i < 5; i++)
        {
            var installState = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            tenant.GitHubInstallState = installState;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (true, installState, null);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _logger.LogDebug(ex, "Retrying install state allocation for tenant {TenantId}.", tenantId);
            }
        }

        return (false, null, "state_allocation_failed");
    }

    public async Task UpdateConfigYamlAsync(Guid tenantId, string? yaml, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return;
        tenant.ConfigYaml = yaml;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantPortalPreferences?> GetPortalPreferencesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var json = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.PortalPreferencesJson)
            .FirstOrDefaultAsync(cancellationToken);
        if (json is null && !await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken))
            return null;
        return PortalPreferencesJson.Parse(json);
    }

    public async Task<TenantPortalPreferences?> UpdatePortalPreferencesAsync(
        Guid tenantId,
        TenantPortalPreferencesPatch patch,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return null;

        var merged = PortalPreferencesJson.Merge(PortalPreferencesJson.Parse(tenant.PortalPreferencesJson), patch);
        tenant.PortalPreferencesJson = PortalPreferencesJson.Serialize(merged);
        await db.SaveChangesAsync(cancellationToken);
        return merged;
    }

    public async Task<TenantAccountSnapshot?> GetAccountSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null)
            return null;

        return new TenantAccountSnapshot(
            t.Id,
            t.Name,
            t.Plan,
            t.Status,
            t.ContactEmail,
            t.GitHubOrgLogin,
            t.GitHubAppInstallationId.HasValue,
            t.GitHubAppInstallationId,
            !string.IsNullOrWhiteSpace(t.ConfigYaml));
    }

    public async Task<string?> GetConfigYamlAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.ConfigYaml)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TenantDashboardAnalytics?> GetDashboardAnalyticsAsync(
        Guid tenantId,
        int days,
        CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 90);
        var endDate = DateTime.UtcNow.Date;
        var startDate = endDate.AddDays(-(days - 1));
        var rangeStart = new DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Utc));
        var rangeEndExclusive = new DateTimeOffset(DateTime.SpecifyKind(endDate.AddDays(1), DateTimeKind.Utc));

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var tenantExists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken);
        if (!tenantExists)
            return null;

        var prDates = await db.PrReviewJobs.AsNoTracking()
            .Where(j =>
                j.Status == ReviewJobStatuses.Completed
                && j.CompletedAt.HasValue
                && j.CompletedAt >= rangeStart
                && j.CompletedAt < rangeEndExclusive)
            .Select(j => j.CompletedAt!.Value.Date)
            .ToListAsync(cancellationToken);

        var violDates = await db.Findings.AsNoTracking()
            .Where(f => f.CreatedAt >= rangeStart && f.CreatedAt < rangeEndExclusive)
            .Select(f => f.CreatedAt.Date)
            .ToListAsync(cancellationToken);

        static Dictionary<DateOnly, int> ToDailyCounts(IEnumerable<DateTime> dates) =>
            dates
                .GroupBy(d => DateOnly.FromDateTime(d))
                .ToDictionary(g => g.Key, g => g.Count());

        var prDict = ToDailyCounts(prDates);
        var violDict = ToDailyCounts(violDates);

        static List<DailyCountDto> BuildSeries(DateOnly start, DateOnly end, Dictionary<DateOnly, int> dict)
        {
            var list = new List<DailyCountDto>();
            for (var d = start; d <= end; d = d.AddDays(1))
                list.Add(new DailyCountDto(d, dict.GetValueOrDefault(d)));
            return list;
        }

        var startDateOnly = DateOnly.FromDateTime(startDate);
        var endDateOnly = DateOnly.FromDateTime(endDate);
        var prSeries = BuildSeries(startDateOnly, endDateOnly, prDict);
        var violSeries = BuildSeries(startDateOnly, endDateOnly, violDict);

        var prsTotal = prDict.Values.Sum();
        var violTotal = violDict.Values.Sum();
        var ratio = prsTotal == 0 ? violTotal : (double)violTotal / prsTotal;
        var drift = (int)Math.Clamp(Math.Round(100 - Math.Min(100, ratio * 12)), 0, 100);

        const string driftNote =
            "Heuristic score 0–100 (higher = healthier): derived from violations per completed PR in the window. Not a substitute for architecture review.";

        return new TenantDashboardAnalytics(
            days,
            prsTotal,
            violTotal,
            prSeries,
            violSeries,
            drift,
            driftNote);
    }

    public async Task<IReadOnlyList<TenantRepositoryRow>> ListRepositoriesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var exists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken);
        if (!exists)
            return Array.Empty<TenantRepositoryRow>();

        return await db.Repositories.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.FullName)
            .Select(r => new TenantRepositoryRow(r.Id, r.FullName, r.Jobs.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantBillingRow?> GetBillingRowAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null)
            return null;

        return new TenantBillingRow(
            t.Id,
            t.Plan,
            t.BillingStatus,
            t.TrialEndsAt,
            t.ContactEmail,
            t.PaystackCustomerCode,
            t.PaystackSubscriptionCode);
    }

    public async Task UpdatePaystackCustomerCodeAsync(
        Guid tenantId,
        string paystackCustomerCode,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return;

        tenant.PaystackCustomerCode = paystackCustomerCode.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyPaidPlanAsync(
        Guid tenantId,
        string plan,
        string? featureFlagsJson,
        string billingStatus,
        string paystackSubscriptionCode,
        string? paystackCustomerCode,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return;

        tenant.Plan = plan.Trim();
        tenant.BillingStatus = billingStatus.Trim();
        tenant.PaystackSubscriptionCode = paystackSubscriptionCode.Trim();
        tenant.TrialEndsAt = null;
        if (!string.IsNullOrWhiteSpace(paystackCustomerCode))
            tenant.PaystackCustomerCode = paystackCustomerCode.Trim();
        if (featureFlagsJson is not null)
            tenant.FeatureFlagsJson = string.IsNullOrWhiteSpace(featureFlagsJson) ? null : featureFlagsJson.Trim();

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Tenant {TenantId} billing updated: plan={Plan}, status={BillingStatus}.",
            tenantId,
            tenant.Plan,
            tenant.BillingStatus);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static string? TruncateDpaIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;
        var t = ip.Trim();
        return t.Length <= 64 ? t : t[..64];
    }
}
