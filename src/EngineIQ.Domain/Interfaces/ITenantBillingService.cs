using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Interfaces;

public interface ITenantBillingService
{
    /// <summary>Creates Paystack customer after registration (skipped for Internal tenants).</summary>
    Task ProvisionCustomerAfterRegisterAsync(
        Guid tenantId,
        string email,
        string companyName,
        CancellationToken cancellationToken = default);

    Task<TenantBillingSnapshot?> GetBillingAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<SubscriptionCheckoutResult> StartSubscriptionCheckoutAsync(
        Guid tenantId,
        string plan,
        string callbackUrl,
        CancellationToken cancellationToken = default);

    Task<SubscriptionConfirmResult> ConfirmSubscriptionAsync(
        Guid tenantId,
        string reference,
        CancellationToken cancellationToken = default);

    Task<PlanChangeResult> ChangePlanAsync(
        Guid tenantId,
        string newPlan,
        CancellationToken cancellationToken = default);
}

public sealed record TenantBillingSnapshot(
    Guid TenantId,
    string Plan,
    string BillingStatus,
    DateTimeOffset? TrialEndsAt,
    string? PaystackCustomerCode,
    string? PaystackSubscriptionCode,
    bool PaystackRequired);

public sealed record SubscriptionCheckoutResult(string Reference, string AuthorizationUrl);

public sealed record SubscriptionConfirmResult(
    bool Ok,
    string? BillingStatus,
    string? PaystackSubscriptionCode,
    string? Error);

public sealed record PlanChangeResult(bool Ok, string? Plan, string? Error);
