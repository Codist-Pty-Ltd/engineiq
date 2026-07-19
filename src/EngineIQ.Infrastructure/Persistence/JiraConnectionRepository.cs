using System.Security.Cryptography;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class JiraConnectionRepository : IJiraConnectionRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IJiraApiTokenProtector _protector;
    private readonly IOptions<PostgresOptions> _postgres;
    private readonly IOptions<Domain.Trust.TrustOptions> _trust;

    public JiraConnectionRepository(
        IDbContextFactory<EngineIQDbContext> factory,
        IJiraApiTokenProtector protector,
        IOptions<PostgresOptions> postgres,
        IOptions<Domain.Trust.TrustOptions> trust)
    {
        _factory = factory;
        _protector = protector;
        _postgres = postgres;
        _trust = trust;
    }

    public async Task<JiraConnectionRow?> FindByWebhookSecretAsync(
        string webhookSecret,
        CancellationToken cancellationToken = default)
    {
        var cs = _postgres.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Postgres:ConnectionString is not configured.");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, tenant_id, site_base_url, email, api_token_protected, webhook_secret,
                   project_keys_csv, enabled, tenant_status
            FROM public.fn_resolve_jira_connection_by_webhook_secret(@s)
            """,
            conn);
        cmd.Parameters.AddWithValue("s", webhookSecret);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new JiraConnectionRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetBoolean(7),
            reader.GetString(8));
    }

    public async Task<JiraConnectionRow?> GetByIdAsync(
        Guid tenantId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var row = await db.JiraConnections.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == connectionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var tenantStatus = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Status)
            .FirstAsync(cancellationToken);

        return new JiraConnectionRow(
            row.Id,
            row.TenantId,
            row.SiteBaseUrl,
            row.Email,
            row.ApiTokenProtected,
            row.WebhookSecret,
            row.ProjectKeysCsv,
            row.Enabled,
            tenantStatus);
    }

    public async Task<IReadOnlyList<JiraConnectionSummary>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var rows = await db.JiraConnections.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var baseUrl = (_trust.Value.PublicApiBaseUrl ?? "https://api.engineiq.co.za").TrimEnd('/');
        return rows.Select(r => new JiraConnectionSummary(
            r.Id,
            r.SiteBaseUrl,
            r.Email,
            r.ProjectKeysCsv,
            r.Enabled,
            MaskWebhookUrl(baseUrl, r.WebhookSecret),
            r.CreatedAt)).ToList();
    }

    public async Task<JiraConnectionCreated> CreateAsync(
        Guid tenantId,
        string siteBaseUrl,
        string email,
        string apiTokenPlaintext,
        IReadOnlyList<string>? projectKeys,
        CancellationToken cancellationToken = default)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var protectedToken = _protector.Protect(apiTokenPlaintext);
        var csv = projectKeys is { Count: > 0 }
            ? string.Join(',', projectKeys.Select(k => k.Trim()).Where(k => k.Length > 0))
            : null;

        var id = Guid.NewGuid();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        db.JiraConnections.Add(new JiraConnection
        {
            Id = id,
            TenantId = tenantId,
            SiteBaseUrl = siteBaseUrl.Trim().TrimEnd('/'),
            Email = email.Trim(),
            ApiTokenProtected = protectedToken,
            WebhookSecret = secret,
            ProjectKeysCsv = string.IsNullOrWhiteSpace(csv) ? null : csv,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = (_trust.Value.PublicApiBaseUrl ?? "https://api.engineiq.co.za").TrimEnd('/');
        var webhookUrl = $"{baseUrl}/api/v1/webhooks/jira/{secret}";
        return new JiraConnectionCreated(id, webhookUrl, secret);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var deleted = await db.JiraConnections
            .Where(c => c.TenantId == tenantId && c.Id == connectionId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    private static string MaskWebhookUrl(string apiBase, string secret)
    {
        var last4 = secret.Length <= 4 ? "****" : secret[^4..];
        return $"{apiBase}/api/v1/webhooks/jira/…{last4}";
    }
}
