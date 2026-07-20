using System.Text.Json.Serialization;
using EngineIQ.API.Jira;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Jira;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EngineIQ.API.Controllers;

[ApiController]
[Route("api/v1/tenant/{id:guid}/jira-connections")]
[EnableRateLimiting("tenantApi")]
[EnableCors("Portal")]
public sealed class JiraConnectionController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IJiraConnectionRepository _connections;
    private readonly IJiraProjectRepoMappingRepository _mappings;
    private readonly IBacklogBackfillRepository _backfills;
    private readonly IBacklogBackfillJobPublisher _backfillPublisher;
    private readonly IJiraClient _jiraClient;
    private readonly IJiraApiTokenProtector _tokenProtector;
    private readonly JiraClientOptions _jiraOptions;
    private readonly ILogger<JiraConnectionController> _logger;

    public JiraConnectionController(
        ITenantRepository tenants,
        IJiraConnectionRepository connections,
        IJiraProjectRepoMappingRepository mappings,
        IBacklogBackfillRepository backfills,
        IBacklogBackfillJobPublisher backfillPublisher,
        IJiraClient jiraClient,
        IJiraApiTokenProtector tokenProtector,
        IOptions<JiraClientOptions> jiraOptions,
        ILogger<JiraConnectionController> logger)
    {
        _tenants = tenants;
        _connections = connections;
        _mappings = mappings;
        _backfills = backfills;
        _backfillPublisher = backfillPublisher;
        _jiraClient = jiraClient;
        _tokenProtector = tokenProtector;
        _jiraOptions = jiraOptions.Value;
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

    [HttpGet("{connectionId:guid}/mappings")]
    public async Task<ActionResult<JiraMappingsListResponse>> ListMappings(
        Guid id,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var connection = await _connections.GetByIdAsync(id, connectionId, cancellationToken);
        if (connection is null)
            return NotFound();

        var rows = await _mappings.ListByConnectionAsync(id, connectionId, cancellationToken);
        var items = rows
            .GroupBy(r => r.ProjectKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new JiraMappingRowResponse(
                g.Key,
                g.Select(x => new JiraMappingRepoResponse(x.RepositoryId, x.RepositoryFullName)).ToList()))
            .OrderBy(x => x.ProjectKey)
            .ToList();

        return Ok(new JiraMappingsListResponse(items));
    }

    [HttpPut("{connectionId:guid}/mappings")]
    public async Task<IActionResult> ReplaceMappings(
        Guid id,
        Guid connectionId,
        [FromBody] List<JiraMappingPutRequest>? body,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var connection = await _connections.GetByIdAsync(id, connectionId, cancellationToken);
        if (connection is null)
            return NotFound();

        body ??= new List<JiraMappingPutRequest>();
        var tenantRepos = await _tenants.ListRepositoriesAsync(id, cancellationToken);
        var tenantRepoIds = tenantRepos.Select(r => r.Id).ToHashSet();

        var inputs = new List<JiraProjectMappingInput>();
        foreach (var item in body)
        {
            if (string.IsNullOrWhiteSpace(item.ProjectKey))
                return BadRequest(new { error = "project_key_required" });

            var repoIds = item.RepositoryIds ?? new List<Guid>();
            foreach (var repoId in repoIds)
            {
                if (!tenantRepoIds.Contains(repoId))
                    return BadRequest(new { error = "repository_not_in_tenant", repository_id = repoId });
            }

            inputs.Add(new JiraProjectMappingInput(item.ProjectKey.Trim(), repoIds));
        }

        await _mappings.ReplaceAsync(id, connectionId, inputs, cancellationToken);
        _logger.LogInformation(
            "Replaced Jira project mappings for connection {ConnectionId} tenant {TenantId} ({Count} project rows).",
            connectionId,
            id,
            inputs.Count);

        return NoContent();
    }

    [HttpPost("{connectionId:guid}/backfill")]
    public async Task<IActionResult> StartBackfill(
        Guid id,
        Guid connectionId,
        [FromBody] BackfillRequest? body,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var connection = await _connections.GetByIdAsync(id, connectionId, cancellationToken);
        if (connection is null)
            return NotFound();

        body ??= new BackfillRequest();
        var mappingRows = await _mappings.ListByConnectionAsync(id, connectionId, cancellationToken);
        var mappedKeys = mappingRows.Select(r => r.ProjectKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var effectiveJql = BackfillJqlBuilder.BuildEffectiveJql(
            body.Jql,
            connection.ProjectKeysCsv,
            mappedKeys,
            out var jqlError);
        if (effectiveJql is null)
            return BadRequest(new { error = jqlError ?? "invalid_jql", message = "No project keys on the connection or mappings; supply jql or configure project keys." });

        var maxIssues = body.MaxIssues ?? _jiraOptions.MaxBackfillIssues;
        maxIssues = Math.Clamp(maxIssues, 1, Math.Max(1, _jiraOptions.MaxBackfillIssues));

        string apiToken;
        try
        {
            apiToken = _tokenProtector.Unprotect(connection.ApiTokenProtected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt Jira token for connection {ConnectionId}.", connectionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "token_unprotect_failed" });
        }

        var jiraInfo = new JiraConnectionInfo(connection.SiteBaseUrl, connection.Email, apiToken);

        if (body.DryRun)
        {
            try
            {
                var page = await _jiraClient.SearchIssuesAsync(jiraInfo, effectiveJql, 0, 1, cancellationToken);
                return Ok(new BackfillDryRunResponse(
                    page.Total,
                    effectiveJql,
                    Math.Min(page.Total, maxIssues)));
            }
            catch (InvalidJqlException ex)
            {
                return BadRequest(new { error = "invalid_jql", message = ex.Message });
            }
        }

        if (await _backfills.FindActiveJobIdAsync(id, connectionId, cancellationToken) is { } activeId)
            return Conflict(new { error = "backfill_in_progress", job_id = activeId });

        var enqueue = await _backfills.TryCreateQueuedAsync(id, connectionId, effectiveJql, maxIssues, cancellationToken);
        if (!enqueue.Created && !enqueue.NeedsRepublish)
        {
            if (string.Equals(enqueue.BlockReason, "in_progress", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "backfill_in_progress", job_id = enqueue.JobId });
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "enqueue_failed" });
        }

        var message = new BacklogBackfillJobMessage(id, enqueue.JobId!.Value, connectionId, Attempt: 0);
        try
        {
            await _backfillPublisher.PublishAsync(message, cancellationToken);
            await _backfills.MarkQueuedAfterPublishAsync(id, enqueue.JobId.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish backfill job {JobId}; remains PendingPublish.", enqueue.JobId);
        }

        return Accepted(new { job_id = enqueue.JobId, jql = effectiveJql, max_issues = maxIssues });
    }

    [HttpGet("{connectionId:guid}/backfill/{backfillId:guid}")]
    public async Task<ActionResult<BackfillStatusResponse>> GetBackfill(
        Guid id,
        Guid connectionId,
        Guid backfillId,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        var row = await _backfills.GetByIdAsync(id, backfillId, cancellationToken);
        if (row is null || row.JiraConnectionId != connectionId)
            return NotFound();

        return Ok(new BackfillStatusResponse(
            row.Id,
            row.Status,
            row.Jql,
            row.MatchedTotal,
            row.EnqueuedCount,
            row.SkippedCount,
            row.StartAtCursor,
            row.MaxIssues,
            row.CreatedAt,
            row.CompletedAt,
            row.FailureReason));
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

    public sealed class JiraMappingPutRequest
    {
        [JsonPropertyName("projectKey")]
        public string ProjectKey { get; set; } = string.Empty;

        [JsonPropertyName("repositoryIds")]
        public List<Guid>? RepositoryIds { get; set; }
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

    public sealed record JiraMappingRepoResponse(
        [property: JsonPropertyName("repositoryId")] Guid RepositoryId,
        [property: JsonPropertyName("fullName")] string FullName);

    public sealed record JiraMappingRowResponse(
        [property: JsonPropertyName("projectKey")] string ProjectKey,
        [property: JsonPropertyName("repositories")] IReadOnlyList<JiraMappingRepoResponse> Repositories);

    public sealed record JiraMappingsListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<JiraMappingRowResponse> Items);

    public sealed class BackfillRequest
    {
        [JsonPropertyName("jql")]
        public string? Jql { get; set; }

        [JsonPropertyName("dryRun")]
        public bool DryRun { get; set; }

        [JsonPropertyName("maxIssues")]
        public int? MaxIssues { get; set; }
    }

    public sealed record BackfillDryRunResponse(
        [property: JsonPropertyName("matchedTotal")] int MatchedTotal,
        [property: JsonPropertyName("jql")] string Jql,
        [property: JsonPropertyName("wouldAnalyze")] int WouldAnalyze);

    public sealed record BackfillStatusResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("jql")] string Jql,
        [property: JsonPropertyName("matchedTotal")] int MatchedTotal,
        [property: JsonPropertyName("enqueuedCount")] int EnqueuedCount,
        [property: JsonPropertyName("skippedCount")] int SkippedCount,
        [property: JsonPropertyName("startAtCursor")] int StartAtCursor,
        [property: JsonPropertyName("maxIssues")] int MaxIssues,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
        [property: JsonPropertyName("failureReason")] string? FailureReason);
}
