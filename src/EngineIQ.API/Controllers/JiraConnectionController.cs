using System.Text.Json.Serialization;
using EngineIQ.Domain.Interfaces;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EngineIQ.API.Controllers;

[ApiController]
[Route("api/v1/tenant/{id:guid}/jira-connections")]
[EnableRateLimiting("tenantApi")]
[EnableCors("Portal")]
public sealed class JiraConnectionController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IJiraConnectionRepository _connections;
    private readonly ILogger<JiraConnectionController> _logger;

    public JiraConnectionController(
        ITenantRepository tenants,
        IJiraConnectionRepository connections,
        ILogger<JiraConnectionController> logger)
    {
        _tenants = tenants;
        _connections = connections;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<JiraConnectionsListResponse>> List(Guid id, CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var rows = await _connections.ListByTenantAsync(id, cancellationToken);
        var items = rows.Select(r => new JiraConnectionRowResponse(
            r.Id,
            r.SiteBaseUrl,
            r.Email,
            r.ProjectKeysCsv,
            r.Enabled,
            r.WebhookUrlMasked,
            r.CreatedAt)).ToList();

        return Ok(new JiraConnectionsListResponse(items));
    }

    [HttpPost]
    public async Task<ActionResult<JiraConnectionCreatedResponse>> Create(
        Guid id,
        [FromBody] JiraConnectionCreateRequest body,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(body.SiteBaseUrl))
            return BadRequest(new { error = "site_base_url_required" });
        if (string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { error = "email_required" });
        if (string.IsNullOrWhiteSpace(body.ApiToken))
            return BadRequest(new { error = "api_token_required" });

        try
        {
            var created = await _connections.CreateAsync(
                id,
                body.SiteBaseUrl.Trim(),
                body.Email.Trim(),
                body.ApiToken,
                body.ProjectKeys,
                cancellationToken);

            _logger.LogInformation("Created Jira connection {ConnectionId} for tenant {TenantId}.", created.Id, id);

            return Ok(new JiraConnectionCreatedResponse(
                created.Id,
                created.WebhookUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Jira connection for tenant {TenantId}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "create_failed" });
        }
    }

    [HttpDelete("{connectionId:guid}")]
    public async Task<IActionResult> Delete(Guid id, Guid connectionId, CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var deleted = await _connections.DeleteAsync(id, connectionId, cancellationToken);
        if (!deleted)
            return NotFound();

        _logger.LogInformation("Deleted Jira connection {ConnectionId} for tenant {TenantId}.", connectionId, id);
        return NoContent();
    }

    public sealed class JiraConnectionCreateRequest
    {
        [JsonPropertyName("site_base_url")]
        public string SiteBaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = string.Empty;

        [JsonPropertyName("project_keys")]
        public List<string>? ProjectKeys { get; set; }
    }

    public sealed record JiraConnectionCreatedResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("webhook_url")] string WebhookUrl);

    public sealed record JiraConnectionRowResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("site_base_url")] string SiteBaseUrl,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("project_keys_csv")] string? ProjectKeysCsv,
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("webhook_url")] string WebhookUrlMasked,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    public sealed record JiraConnectionsListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<JiraConnectionRowResponse> Items);
}
