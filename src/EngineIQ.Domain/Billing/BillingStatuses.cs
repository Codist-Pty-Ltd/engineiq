namespace EngineIQ.Domain.Billing;

public static class BillingStatuses
{
    public const string Trialing = "Trialing";
    public const string Active = "Active";
    public const string PastDue = "PastDue";
    public const string Cancelled = "Cancelled";

    /// <summary>Codist dogfood / golden-four — no Paystack, never auto-suspended for billing.</summary>
    public const string Internal = "Internal";
}
