namespace EngineIQ.Infrastructure.Paystack;

public sealed class PaystackOptions
{
    public const string SectionName = "Paystack";

    public string SecretKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string PlanStarter { get; set; } = string.Empty;

    public string PlanGrowth { get; set; } = string.Empty;

    public string PlanScale { get; set; } = string.Empty;

    public string ResolvePlanCode(string planKeySuffix) =>
        planKeySuffix.Trim().ToLowerInvariant() switch
        {
            "starter" => PlanStarter,
            "growth" => PlanGrowth,
            "scale" => PlanScale,
            _ => string.Empty,
        };
}
