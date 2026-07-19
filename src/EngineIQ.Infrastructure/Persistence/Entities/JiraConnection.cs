namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class JiraConnection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SiteBaseUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ApiTokenProtected { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string? ProjectKeysCsv { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
}
