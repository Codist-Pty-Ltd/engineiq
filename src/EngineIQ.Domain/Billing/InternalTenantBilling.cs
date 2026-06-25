namespace EngineIQ.Domain.Billing;

/// <summary>
/// Long-lived Codist personas and dogfood tenants that bypass Paystack billing.
/// </summary>
public static class InternalTenantBilling
{
    private static readonly HashSet<string> InternalEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello@mybillable.co.za",
        "hello@therecord.co.za",
        "hello@skillbay.co.za",
        "technical@codist.co.za",
        "hello@codist.co.za",
    };

    public static bool IsInternalEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && InternalEmails.Contains(email.Trim());

    public static bool BypassesPaystack(string? billingStatus) =>
        string.Equals(billingStatus, BillingStatuses.Internal, StringComparison.OrdinalIgnoreCase);
}
