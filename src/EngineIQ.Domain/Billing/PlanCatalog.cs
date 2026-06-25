namespace EngineIQ.Domain.Billing;

/// <summary>Product plan names and Paystack mapping helpers.</summary>
public static class PlanCatalog
{
    public const string Starter = "Starter";
    public const string Growth = "Growth";
    public const string Scale = "Scale";
    public const string Enterprise = "Enterprise";

    public static bool IsKnownProductPlan(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        var p = plan.Trim();
        return p.Equals(Starter, StringComparison.OrdinalIgnoreCase)
               || p.Equals(Growth, StringComparison.OrdinalIgnoreCase)
               || p.Equals(Scale, StringComparison.OrdinalIgnoreCase)
               || p.Equals(Enterprise, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps portal/marketing plan to Paystack env key suffix (Starter, Growth, Scale).</summary>
    public static string PaystackPlanKeySuffix(string plan)
    {
        var p = plan.Trim();
        if (p.Equals(Enterprise, StringComparison.OrdinalIgnoreCase))
            return Scale;
        if (p.Equals(Scale, StringComparison.OrdinalIgnoreCase))
            return Scale;
        if (p.Equals(Growth, StringComparison.OrdinalIgnoreCase))
            return Growth;
        return Starter;
    }

    public static string? DefaultFeatureFlagsJson(string plan)
    {
        var key = PaystackPlanKeySuffix(plan);
        return key switch
        {
            Starter => """{"max_repos":5}""",
            Growth => """{"max_repos":25,"advanced_analytics":true}""",
            Scale => """{"max_repos":-1,"advanced_analytics":true,"priority_support":true}""",
            _ => null,
        };
    }
}
