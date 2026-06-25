using System.Diagnostics;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Models;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;
using EngineIQ.GitHub;
using EngineIQ.Observability;
using Microsoft.AspNetCore.Mvc;

namespace EngineIQ.API.Controllers;

/// <summary>
/// GitHub App deliveries are signed with the <b>single</b> <c>GitHub:WebhookSecret</c> from the App settings.
/// Each tenant also receives a unique <c>whsec_…</c> value at registration (hash-only in DB); it is for customer records
/// and is <b>not</b> part of GitHub&apos;s HMAC (GitHub does not support per-installation webhook secrets on App webhooks).
/// </summary>
[ApiController]
[Route("webhooks")]
public class WebhookController : ControllerBase
{
    private const string GitHubDeliveryHeader = "X-GitHub-Delivery";

    private readonly IPullReviewJobPublisher _publisher;
    private readonly IJobRepository _jobRepository;
    private readonly ITenantRepository _tenants;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IPullReviewJobPublisher publisher,
        IJobRepository jobRepository,
        ITenantRepository tenants,
        IConfiguration configuration,
        ILogger<WebhookController> logger)
    {
        _publisher = publisher;
        _jobRepository = jobRepository;
        _tenants = tenants;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("github")]
    public async Task<IActionResult> GitHub(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ReviewTelemetry.StartActivity("review.webhook", ActivityKind.Server);

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payloadBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var signatureHeader = Request.Headers[WebhookValidator.SignatureHeaderName].FirstOrDefault();
        var webhookSecret = _configuration["GitHub:WebhookSecret"] ?? string.Empty;

        if (!WebhookValidator.ValidateSignature(payloadBody, signatureHeader ?? string.Empty, webhookSecret))
        {
            _logger.LogWarning("GitHub webhook signature validation failed.");
            return Unauthorized();
        }

        var deliveryId = Request.Headers[GitHubDeliveryHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(deliveryId))
        {
            _logger.LogWarning("Missing {Header} header.", GitHubDeliveryHeader);
            return BadRequest();
        }

        var payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(payloadBody);
        if (payload?.Action is null || payload.Installation is null || payload.PullRequest is null || payload.Repository is null)
        {
            _logger.LogWarning("Invalid webhook payload structure.");
            return BadRequest();
        }

        if (payload.Action is not ("opened" or "synchronize" or "reopened"))
        {
            _logger.LogInformation("Ignoring GitHub event action: {Action}", payload.Action);
            return Ok();
        }

        var installationId = payload.Installation.Id;
        var fullName = payload.Repository.FullName ?? payload.Repository.Name ?? "";
        var (owner, repo) = ParseOwnerRepo(fullName);
        var prNumber = payload.PullRequest.Number;

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            _logger.LogWarning("Could not parse repository owner/name.");
            return BadRequest();
        }

        var isDraft = payload.PullRequest.Draft;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(450));

        PrJobEnqueueResult enqueue;
        try
        {
            enqueue = await _jobRepository.TryCreateQueuedJobAsync(
                installationId,
                fullName,
                prNumber,
                deliveryId,
                budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job enqueue exceeded time budget ({Ms} ms).", sw.ElapsedMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable");
        }

        if (enqueue.TenantId is null)
        {
            _logger.LogWarning("Unknown GitHub installation {InstallationId}.", installationId);
            return NotFound("unknown_installation");
        }

        if (!enqueue.Created && !enqueue.NeedsRepublish)
        {
            if (string.Equals(enqueue.BlockReason, "suspended", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("GitHub webhook rejected for suspended tenant {TenantId}.", enqueue.TenantId);
                ReviewTelemetry.RecordWebhookEnqueue("suspended", sw.Elapsed.TotalMilliseconds);
                return StatusCode(StatusCodes.Status403Forbidden, "tenant_suspended");
            }

            if (string.Equals(enqueue.BlockReason, "enqueue_failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Job enqueue exhausted for tenant {TenantId}.", enqueue.TenantId);
                ReviewTelemetry.RecordWebhookEnqueue("enqueue_failed", sw.Elapsed.TotalMilliseconds);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "enqueue_failed");
            }

            _logger.LogInformation("Duplicate GitHub delivery {DeliveryId}; skipping enqueue.", deliveryId);
            ReviewTelemetry.RecordWebhookEnqueue("duplicate", sw.Elapsed.TotalMilliseconds);
            return Ok();
        }

        if (enqueue.NeedsRepublish)
        {
            _logger.LogInformation(
                "GitHub delivery {DeliveryId} still pending RabbitMQ publish; retrying enqueue.",
                deliveryId);
        }

        using var logScope = ReviewLogScope.Begin(
            _logger,
            enqueue.TenantId!.Value,
            enqueue.JobId!.Value,
            prNumber,
            owner,
            repo);
        ReviewTelemetry.SetReviewTags(activity, enqueue.TenantId.Value, enqueue.JobId.Value, prNumber);
        activity?.SetTag("github.delivery_id", deliveryId);

        var preferences = await _tenants.GetPortalPreferencesAsync(enqueue.TenantId!.Value, budget.Token)
                        ?? new TenantPortalPreferences();
        if (!ReviewEnqueuePolicy.ShouldEnqueue(preferences, isDraft, out var skipReason))
        {
            await _jobRepository.MarkJobSkippedAsync(
                enqueue.TenantId!.Value,
                enqueue.JobId!.Value,
                skipReason,
                cancellationToken);
            _logger.LogInformation(
                "Skipping review enqueue for tenant {TenantId} PR {Repo}#{Pr}: {Reason}",
                enqueue.TenantId,
                fullName,
                prNumber,
                skipReason);
            return Ok();
        }

        var jobMessage = new PullReviewJobMessage(
            enqueue.TenantId.Value,
            enqueue.JobId!.Value,
            enqueue.RepositoryId!.Value,
            installationId,
            owner,
            repo,
            prNumber,
            Attempt: 0);

        try
        {
            using var enqueueActivity = ReviewTelemetry.StartActivity("review.enqueue");
            ReviewTelemetry.SetReviewTags(enqueueActivity, enqueue.TenantId.Value, enqueue.JobId.Value, prNumber);
            await _publisher.PublishAsync(jobMessage, budget.Token);
            await _jobRepository.MarkJobQueuedAfterPublishAsync(
                enqueue.TenantId.Value,
                enqueue.JobId.Value,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "RabbitMQ publish exceeded time budget ({Ms} ms); job {JobId} remains PendingPublish for reconciler/GitHub redelivery.",
                sw.ElapsedMilliseconds,
                enqueue.JobId);
            ReviewTelemetry.RecordWebhookEnqueue("publish_failed", sw.Elapsed.TotalMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enqueue PR review job {JobId}; row remains PendingPublish for reconciler/GitHub redelivery.",
                enqueue.JobId);
            ReviewTelemetry.RecordWebhookEnqueue("publish_failed", sw.Elapsed.TotalMilliseconds);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "enqueue_failed");
        }

        sw.Stop();
        ReviewTelemetry.RecordWebhookEnqueue("enqueued", sw.Elapsed.TotalMilliseconds);
        if (sw.ElapsedMilliseconds > 500)
            _logger.LogWarning("Webhook handler took {Ms} ms (target under 500 ms).", sw.ElapsedMilliseconds);
        else
            _logger.LogDebug("Webhook enqueued in {Ms} ms.", sw.ElapsedMilliseconds);

        return Ok();
    }

    private static (string Owner, string Repo) ParseOwnerRepo(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return ("", "");
        return (parts[0], parts[1]);
    }
}
