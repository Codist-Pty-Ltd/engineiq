using System.Diagnostics;
using System.Text.Json;
using EngineIQ.API.Jira;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Jira;
using EngineIQ.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EngineIQ.API.Controllers;

/// <summary>
/// Jira Cloud admin webhooks are unsigned; authentication is the per-connection secret in the URL path.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public sealed class JiraWebhookController : ControllerBase
{
    private readonly JiraWebhookValidator _validator;
    private readonly IIssueAnalysisJobRepository _jobs;
    private readonly IJiraIssueAnalysisJobPublisher _publisher;
    private readonly JiraClientOptions _jiraOptions;
    private readonly ILogger<JiraWebhookController> _logger;

    public JiraWebhookController(
        JiraWebhookValidator validator,
        IIssueAnalysisJobRepository jobs,
        IJiraIssueAnalysisJobPublisher publisher,
        IOptions<JiraClientOptions> jiraOptions,
        ILogger<JiraWebhookController> logger)
    {
        _validator = validator;
        _jobs = jobs;
        _publisher = publisher;
        _jiraOptions = jiraOptions.Value;
        _logger = logger;
    }

    [HttpPost("jira/{webhookSecret}")]
    public async Task<IActionResult> Receive(string webhookSecret, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ReviewTelemetry.StartActivity("jira.webhook", ActivityKind.Server);

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payloadBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(450));

        JiraConnectionRow? connection;
        try
        {
            connection = await _validator.TryResolveConnectionAsync(webhookSecret, budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Jira webhook resolve exceeded time budget ({Ms} ms).", sw.ElapsedMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable");
        }

        if (connection is null)
        {
            _logger.LogWarning("Unknown Jira webhook secret.");
            return NotFound("unknown_connection");
        }

        if (!connection.Enabled)
        {
            _logger.LogWarning("Jira webhook rejected for disabled connection {ConnectionId}.", connection.Id);
            return StatusCode(StatusCodes.Status403Forbidden, "connection_disabled");
        }

        if (string.Equals(connection.TenantStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Jira webhook rejected for suspended tenant {TenantId}.", connection.TenantId);
            ReviewTelemetry.RecordWebhookEnqueue("suspended", sw.Elapsed.TotalMilliseconds);
            return StatusCode(StatusCodes.Status403Forbidden, "tenant_suspended");
        }

        if (!TryParsePayload(payloadBody, out var parsed) || parsed is null)
        {
            _logger.LogWarning("Invalid Jira webhook payload structure.");
            return BadRequest();
        }

        var triggerLabel = string.IsNullOrWhiteSpace(_jiraOptions.TriggerLabel) ? "engineiq" : _jiraOptions.TriggerLabel;
        var labelAdded = JiraWebhookEventFilter.WasTriggerLabelAdded(parsed.ChangelogItems, triggerLabel);
        var trigger = string.Equals(parsed.WebhookEvent, JiraWebhookEventFilter.IssueUpdatedEvent, StringComparison.OrdinalIgnoreCase)
            ? AnalysisTrigger.Label
            : AnalysisTrigger.Created;

        if (!JiraWebhookEventFilter.ShouldEnqueue(
                parsed.WebhookEvent,
                parsed.IssueTypeName,
                parsed.IsSubtask,
                parsed.ProjectKey,
                connection.ProjectKeysCsv,
                out var skipReason,
                labelTriggerAdded: labelAdded))
        {
            _logger.LogInformation("Ignoring Jira webhook: {Reason}", skipReason);
            return Ok();
        }

        var dedupeKey = trigger == AnalysisTrigger.Label
            ? JiraWebhookEventFilter.BuildLabelDedupeKey(parsed.IssueId, parsed.Updated)
            : JiraWebhookEventFilter.BuildDedupeKey(parsed.IssueId, parsed.Updated);

        IssueAnalysisJobEnqueueResult enqueue;
        try
        {
            enqueue = await _jobs.TryCreateQueuedJobAsync(
                connection.TenantId,
                connection.Id,
                parsed.IssueKey,
                parsed.IssueId,
                dedupeKey,
                budget.Token,
                trigger);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Jira job enqueue exceeded time budget ({Ms} ms).", sw.ElapsedMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable");
        }

        if (!enqueue.Created && !enqueue.NeedsRepublish)
        {
            if (string.Equals(enqueue.BlockReason, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Jira webhook rejected for suspended tenant {TenantId}.", enqueue.TenantId);
                ReviewTelemetry.RecordWebhookEnqueue("suspended", sw.Elapsed.TotalMilliseconds);
                return StatusCode(StatusCodes.Status403Forbidden, "tenant_suspended");
            }

            if (string.Equals(enqueue.BlockReason, "enqueue_failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Jira job enqueue exhausted for tenant {TenantId}.", enqueue.TenantId);
                ReviewTelemetry.RecordWebhookEnqueue("enqueue_failed", sw.Elapsed.TotalMilliseconds);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "enqueue_failed");
            }

            _logger.LogInformation("Duplicate Jira delivery for {IssueKey}; skipping enqueue.", parsed.IssueKey);
            ReviewTelemetry.RecordWebhookEnqueue("duplicate", sw.Elapsed.TotalMilliseconds);
            return Ok();
        }

        if (enqueue.NeedsRepublish)
        {
            _logger.LogInformation(
                "Jira issue {IssueKey} still pending RabbitMQ publish; retrying enqueue.",
                parsed.IssueKey);
        }

        activity?.SetTag("tenant.id", enqueue.TenantId);
        activity?.SetTag("job.id", enqueue.JobId);
        activity?.SetTag("jira.issue_key", parsed.IssueKey);

        var jobMessage = new JiraIssueAnalysisJobMessage(
            enqueue.TenantId!.Value,
            enqueue.JobId!.Value,
            enqueue.JiraConnectionId!.Value,
            parsed.IssueKey,
            parsed.IssueId,
            Attempt: 0,
            Trigger: trigger);

        try
        {
            using var enqueueActivity = ReviewTelemetry.StartActivity("jira.enqueue");
            await _publisher.PublishAsync(jobMessage, budget.Token);
            await _jobs.MarkJobQueuedAfterPublishAsync(
                enqueue.TenantId.Value,
                enqueue.JobId.Value,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "RabbitMQ Jira publish exceeded time budget ({Ms} ms); job {JobId} remains PendingPublish.",
                sw.ElapsedMilliseconds,
                enqueue.JobId);
            ReviewTelemetry.RecordWebhookEnqueue("publish_failed", sw.Elapsed.TotalMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "PendingPublish");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enqueue Jira issue analysis job {JobId}; row remains PendingPublish.",
                enqueue.JobId);
            ReviewTelemetry.RecordWebhookEnqueue("publish_failed", sw.Elapsed.TotalMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "PendingPublish");
        }

        sw.Stop();
        ReviewTelemetry.RecordWebhookEnqueue("enqueued", sw.Elapsed.TotalMilliseconds);
        if (sw.ElapsedMilliseconds > 500)
            _logger.LogWarning("Jira webhook handler took {Ms} ms (target under 500 ms).", sw.ElapsedMilliseconds);
        else
            _logger.LogDebug("Jira webhook enqueued in {Ms} ms.", sw.ElapsedMilliseconds);

        return Ok();
    }

    private static bool TryParsePayload(string payloadBody, out JiraWebhookMinimalPayload? payload)
    {
        payload = null;
        try
        {
            using var doc = JsonDocument.Parse(payloadBody);
            var root = doc.RootElement;
            var webhookEvent = root.TryGetProperty("webhookEvent", out var we) ? we.GetString() : null;

            if (!root.TryGetProperty("issue", out var issue))
                return false;

            if (!issue.TryGetProperty("id", out var idEl))
                return false;

            long issueId;
            if (idEl.ValueKind == JsonValueKind.Number)
                issueId = idEl.GetInt64();
            else if (idEl.ValueKind == JsonValueKind.String && long.TryParse(idEl.GetString(), out var parsedId))
                issueId = parsedId;
            else
                return false;

            var issueKey = issue.TryGetProperty("key", out var keyEl) ? keyEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(issueKey))
                return false;

            string? issueTypeName = null;
            var isSubtask = false;
            string? projectKey = null;
            string? updated = null;

            if (issue.TryGetProperty("fields", out var fields))
            {
                if (fields.TryGetProperty("issuetype", out var issueType))
                {
                    if (issueType.TryGetProperty("name", out var nameEl))
                        issueTypeName = nameEl.GetString();
                    if (issueType.TryGetProperty("subtask", out var subEl))
                        isSubtask = subEl.ValueKind == JsonValueKind.True;
                }

                if (fields.TryGetProperty("project", out var project)
                    && project.TryGetProperty("key", out var pkEl))
                {
                    projectKey = pkEl.GetString();
                }

                if (fields.TryGetProperty("updated", out var updatedEl))
                    updated = updatedEl.ValueKind == JsonValueKind.String
                        ? updatedEl.GetString()
                        : updatedEl.GetRawText();
            }

            var changelogItems = ParseChangelogItems(root);

            payload = new JiraWebhookMinimalPayload(
                webhookEvent,
                issueId,
                issueKey!,
                issueTypeName,
                projectKey,
                updated,
                isSubtask,
                changelogItems);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<JiraChangelogLabelItem> ParseChangelogItems(JsonElement root)
    {
        if (!root.TryGetProperty("changelog", out var changelog)
            || !changelog.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JiraChangelogLabelItem>();
        }

        var list = new List<JiraChangelogLabelItem>();
        foreach (var item in items.EnumerateArray())
        {
            var field = item.TryGetProperty("field", out var f) ? f.GetString() : null;
            var fromString = item.TryGetProperty("fromString", out var fs)
                ? (fs.ValueKind == JsonValueKind.String ? fs.GetString() : fs.GetRawText())
                : null;
            var toString = item.TryGetProperty("toString", out var ts)
                ? (ts.ValueKind == JsonValueKind.String ? ts.GetString() : ts.GetRawText())
                : null;
            list.Add(new JiraChangelogLabelItem(field, fromString, toString));
        }

        return list;
    }

    private sealed record JiraWebhookMinimalPayload(
        string? WebhookEvent,
        long IssueId,
        string IssueKey,
        string? IssueTypeName,
        string? ProjectKey,
        string? Updated,
        bool IsSubtask,
        IReadOnlyList<JiraChangelogLabelItem> ChangelogItems);
}
