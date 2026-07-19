using EngineIQ.Domain.Jira;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Per-tenant Jira Cloud connections. Webhook secret lookup is cross-tenant (SECURITY DEFINER).</summary>
public interface IJiraConnectionRepository
{
    Task<JiraConnectionRow?> FindByWebhookSecretAsync(string webhookSecret, CancellationToken cancellationToken = default);

    Task<JiraConnectionRow?> GetByIdAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JiraConnectionSummary>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<JiraConnectionCreated> CreateAsync(
        Guid tenantId,
        string siteBaseUrl,
        string email,
        string apiTokenPlaintext,
        IReadOnlyList<string>? projectKeys,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken = default);
}

public sealed record JiraConnectionRow(
    Guid Id,
    Guid TenantId,
    string SiteBaseUrl,
    string Email,
    string ApiTokenProtected,
    string WebhookSecret,
    string? ProjectKeysCsv,
    bool Enabled,
    string TenantStatus);

public sealed record JiraConnectionSummary(
    Guid Id,
    string SiteBaseUrl,
    string Email,
    string? ProjectKeysCsv,
    bool Enabled,
    string WebhookUrlMasked,
    DateTimeOffset CreatedAt);

public sealed record JiraConnectionCreated(
    Guid Id,
    string WebhookUrl,
    string WebhookSecret);
