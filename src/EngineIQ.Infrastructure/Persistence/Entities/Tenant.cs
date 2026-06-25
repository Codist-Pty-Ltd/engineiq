namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public long? GitHubOrgId { get; set; }
    public string? GitHubOrgLogin { get; set; }
    public long? GitHubAppInstallationId { get; set; }
    public byte[]? WebhookSecretHash { get; set; }
    public byte[]? ApiKeyHash { get; set; }
    /// <summary>One-time value embedded in the GitHub App install URL; cleared after install callback.</summary>
    public string? GitHubInstallState { get; set; }
    public string? ContactEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ConfigYaml { get; set; }
    public string? FeatureFlagsJson { get; set; }
    /// <summary>Portal review + notification preferences (jsonb, snake_case keys).</summary>
    public string? PortalPreferencesJson { get; set; }
    public DateTimeOffset? DpaAcceptedAt { get; set; }
    public string? DpaAcceptedIp { get; set; }

    public string? PaystackCustomerCode { get; set; }
    public string? PaystackSubscriptionCode { get; set; }
    public string BillingStatus { get; set; } = string.Empty;
    public DateTimeOffset? TrialEndsAt { get; set; }

    public ICollection<Repository> Repositories { get; set; } = new List<Repository>();
}
