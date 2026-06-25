namespace EngineIQ.Domain.Tenants;

public sealed record TenantBillingRow(
    Guid TenantId,
    string Plan,
    string BillingStatus,
    DateTimeOffset? TrialEndsAt,
    string? ContactEmail,
    string? PaystackCustomerCode,
    string? PaystackSubscriptionCode);
