using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Interfaces;

public interface ITenantRepository
{
    Task<RegisterTenantResult> RegisterAsync(RegisterTenantCommand command, CancellationToken cancellationToken = default);

    /// <summary>Links GitHub App installation to the tenant identified by the one-time <paramref name="installState"/> from the install URL.</summary>
    Task<(bool Ok, Guid? TenantId, string? ContactEmail, string? Error)> CompleteGitHubInstallAsync(
        long installationId,
        string installState,
        CancellationToken cancellationToken = default);

    Task<Guid?> ValidateApiKeyAndGetTenantIdAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Issues a new API key (plaintext returned once) and invalidates the previous hash.</summary>
    Task<(bool Ok, string? ApiKeyPlaintext)> RotateApiKeyAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantStatusSnapshot?> GetStatusSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a one-time install state when the tenant has no GitHub App installation yet.
    /// Creates a new state if the welcome email state was consumed or missing.
    /// </summary>
    Task<(bool Ok, string? InstallState, string? Error)> EnsureGitHubInstallStateAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task UpdateConfigYamlAsync(Guid tenantId, string? yaml, CancellationToken cancellationToken = default);

    Task<TenantPortalPreferences?> GetPortalPreferencesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantPortalPreferences?> UpdatePortalPreferencesAsync(
        Guid tenantId,
        TenantPortalPreferencesPatch patch,
        CancellationToken cancellationToken = default);

    Task<TenantAccountSnapshot?> GetAccountSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<string?> GetConfigYamlAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantDashboardAnalytics?> GetDashboardAnalyticsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantRepositoryRow>> ListRepositoriesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantBillingRow?> GetBillingRowAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task UpdatePaystackCustomerCodeAsync(
        Guid tenantId,
        string paystackCustomerCode,
        CancellationToken cancellationToken = default);

    Task ApplyPaidPlanAsync(
        Guid tenantId,
        string plan,
        string? featureFlagsJson,
        string billingStatus,
        string paystackSubscriptionCode,
        string? paystackCustomerCode,
        CancellationToken cancellationToken = default);
}
