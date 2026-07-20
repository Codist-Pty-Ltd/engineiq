using System.Text.Json.Serialization;
using EngineIQ.API.Validation;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jobs;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Tenants;
using EngineIQ.GitHub;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EngineIQ.API.Controllers;

[ApiController]
[Route("api/v1/tenant/{id:guid}")]
[EnableRateLimiting("tenantApi")]
[EnableCors("Portal")]
public sealed class TenantController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantBillingService _billing;
    private readonly IFindingRepository _findings;
    private readonly IJobRepository _jobs;
    private readonly IRepositoryRepository _repositories;
    private readonly IRepoIndexJobRepository _indexJobs;
    private readonly IRepoIndexJobPublisher _indexPublisher;
    private readonly IRepoArchiveClient _repoArchive;
    private readonly StandardsConfigYamlValidator _yamlValidator;
    private readonly IOptions<GitHubClientOptions> _gitHub;
    private readonly ILogger<TenantController> _logger;

    public TenantController(
        ITenantRepository tenants,
        ITenantBillingService billing,
        IFindingRepository findings,
        IJobRepository jobs,
        IRepositoryRepository repositories,
        IRepoIndexJobRepository indexJobs,
        IRepoIndexJobPublisher indexPublisher,
        IRepoArchiveClient repoArchive,
        StandardsConfigYamlValidator yamlValidator,
        IOptions<GitHubClientOptions> gitHub,
        ILogger<TenantController> logger)
    {
        _tenants = tenants;
        _billing = billing;
        _findings = findings;
        _jobs = jobs;
        _repositories = repositories;
        _indexJobs = indexJobs;
        _indexPublisher = indexPublisher;
        _repoArchive = repoArchive;
        _yamlValidator = yamlValidator;
        _gitHub = gitHub;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<TenantStatusResponse>> Status(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await _tenants.GetStatusSnapshotAsync(id, cancellationToken);
        if (snapshot is null)
            return NotFound();

        return Ok(new TenantStatusResponse(
            snapshot.OnboardingStatus,
            snapshot.RepositoriesDetected,
            snapshot.FirstPrReviewed));
    }

    [HttpGet("onboarding/install-url")]
    public async Task<ActionResult<TenantInstallUrlResponse>> OnboardingInstallUrl(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (account is null)
            return NotFound();

        if (account.GitHubAppConnected)
            return Conflict(new { error = "already_installed" });

        if (string.IsNullOrWhiteSpace(_gitHub.Value.AppSlug))
        {
            _logger.LogError("GitHub:AppSlug is not configured; cannot build install URL.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "server_misconfigured" });
        }

        var (ok, installState, error) = await _tenants.EnsureGitHubInstallStateAsync(id, cancellationToken);
        if (!ok)
        {
            return error switch
            {
                "already_installed" => Conflict(new { error }),
                "not_found" => NotFound(),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error = error ?? "install_state_failed" }),
            };
        }

        var slug = _gitHub.Value.AppSlug.Trim();
        var installUrl =
            $"https://github.com/apps/{slug}/installations/new?state={Uri.EscapeDataString(installState!)}";

        return Ok(new TenantInstallUrlResponse(installUrl, account.GitHubOrgLogin));
    }

    [HttpGet("billing")]
    public async Task<ActionResult<TenantBillingResponse>> Billing(Guid id, CancellationToken cancellationToken)
    {
        var billing = await _billing.GetBillingAsync(id, cancellationToken);
        if (billing is null)
            return NotFound();

        return Ok(new TenantBillingResponse(
            billing.Plan,
            billing.BillingStatus,
            billing.TrialEndsAt,
            billing.PaystackCustomerCode,
            billing.PaystackSubscriptionCode,
            billing.PaystackRequired));
    }

    [HttpPost("billing/subscribe")]
    public async Task<ActionResult<BillingSubscribeResponse>> BillingSubscribe(
        Guid id,
        [FromBody] BillingSubscribeRequest body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.CallbackUrl))
            return BadRequest(new { error = "callback_url_required" });

        try
        {
            var result = await _billing.StartSubscriptionCheckoutAsync(
                id,
                body.Plan.Trim(),
                body.CallbackUrl.Trim(),
                cancellationToken);
            return Ok(new BillingSubscribeResponse(result.Reference, result.AuthorizationUrl));
        }
        catch (InvalidOperationException ex) when (ex.Message is "billing_not_required")
        {
            return Conflict(new { error = "billing_not_required" });
        }
        catch (InvalidOperationException ex) when (ex.Message is "paystack_not_configured")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "paystack_not_configured" });
        }
        catch (InvalidOperationException ex) when (ex.Message is "unknown_plan")
        {
            return BadRequest(new { error = "unknown_plan" });
        }
        catch (InvalidOperationException ex) when (ex.Message is "paystack_plan_not_configured")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "paystack_plan_not_configured" });
        }
        catch (InvalidOperationException ex) when (ex.Message is "missing_contact_email")
        {
            return BadRequest(new { error = "missing_contact_email" });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("billing/confirm")]
    public async Task<ActionResult<BillingConfirmResponse>> BillingConfirm(
        Guid id,
        [FromBody] BillingConfirmRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _billing.ConfirmSubscriptionAsync(id, body.Reference, cancellationToken);
        if (!result.Ok)
            return BadRequest(new BillingConfirmResponse(false, result.BillingStatus, result.PaystackSubscriptionCode, result.Error));

        return Ok(new BillingConfirmResponse(true, result.BillingStatus, result.PaystackSubscriptionCode, null));
    }

    [HttpPost("billing/change-plan")]
    public async Task<ActionResult<BillingChangePlanResponse>> BillingChangePlan(
        Guid id,
        [FromBody] BillingChangePlanRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _billing.ChangePlanAsync(id, body.Plan.Trim(), cancellationToken);
        if (!result.Ok)
        {
            return result.Error switch
            {
                "billing_not_required" => Conflict(new { error = result.Error }),
                "paystack_not_configured" => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
                "no_active_subscription" => Conflict(new { error = result.Error }),
                "unknown_plan" => BadRequest(new { error = result.Error }),
                "paystack_plan_not_configured" => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
                "tenant_not_found" => NotFound(),
                _ => BadRequest(new BillingChangePlanResponse(false, null, result.Error)),
            };
        }

        return Ok(new BillingChangePlanResponse(true, result.Plan, null));
    }

    [HttpGet("account")]
    public async Task<ActionResult<TenantAccountResponse>> Account(Guid id, CancellationToken cancellationToken)
    {
        var a = await _tenants.GetAccountSnapshotAsync(id, cancellationToken);
        if (a is null)
            return NotFound();

        return Ok(new TenantAccountResponse(
            a.TenantId,
            a.CompanyName,
            a.Plan,
            a.Status,
            a.ContactEmail,
            a.GitHubOrgLogin,
            a.GitHubAppConnected,
            a.GitHubAppInstallationId,
            a.HasConfigYaml));
    }

    [HttpPost("rotate-key")]
    public async Task<ActionResult<TenantRotateKeyResponse>> RotateKey(Guid id, CancellationToken cancellationToken)
    {
        var (ok, apiKey) = await _tenants.RotateApiKeyAsync(id, cancellationToken);
        if (!ok)
            return NotFound();

        _logger.LogInformation("Tenant {TenantId} rotated API key via tenant API.", id);
        return Ok(new TenantRotateKeyResponse(apiKey!));
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<TenantAnalyticsResponse>> Analytics(
        Guid id,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var a = await _tenants.GetDashboardAnalyticsAsync(id, days, cancellationToken);
        if (a is null)
            return NotFound();

        return Ok(new TenantAnalyticsResponse(
            a.Days,
            a.PrsReviewedInPeriod,
            a.ViolationsInPeriod,
            a.PrsReviewedPerDay.Select(d => new DailyCountResponse(d.Date.ToString("yyyy-MM-dd"), d.Count)).ToList(),
            a.ViolationsPerDay.Select(d => new DailyCountResponse(d.Date.ToString("yyyy-MM-dd"), d.Count)).ToList(),
            a.ArchitectureDriftScore,
            a.ArchitectureDriftNote));
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<TenantJobsPageResponse>> Jobs(
        Guid id,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var (items, total) = await _jobs.ListTenantJobsAsync(id, status, skip, take, cancellationToken);
        return Ok(new TenantJobsPageResponse(
            total,
            items.Select(MapJobRow).ToList()));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult<TenantJobRowResponse>> JobDetail(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var row = await _jobs.GetTenantJobAsync(id, jobId, cancellationToken);
        if (row is null)
            return NotFound();

        return Ok(MapJobRow(row));
    }

    [HttpGet("jobs/{jobId:guid}/findings")]
    public async Task<ActionResult<FindingsListResponse>> JobFindings(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        if (await _jobs.GetTenantJobAsync(id, jobId, cancellationToken) is null)
            return NotFound();

        var rows = await _findings.ListByJobAsync(id, jobId, cancellationToken);
        return Ok(new FindingsListResponse(rows.Select(MapFinding).ToList()));
    }

    [HttpGet("repositories")]
    public async Task<ActionResult<IReadOnlyList<TenantRepositoryRowResponse>>> Repositories(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var rows = await _tenants.ListRepositoriesAsync(id, cancellationToken);
        return Ok(rows.Select(r => new TenantRepositoryRowResponse(r.Id, r.FullName, r.JobCount)).ToList());
    }

    /// <summary>Manually triggers a code-index job for the repository's default branch HEAD. Returns 202 once queued.</summary>
    [HttpPost("repositories/{repositoryId:guid}/index")]
    public async Task<IActionResult> TriggerIndex(Guid id, Guid repositoryId, CancellationToken cancellationToken)
    {
        var repository = await _repositories.GetByIdAsync(id, repositoryId, cancellationToken);
        if (repository is null)
            return NotFound();

        if (await _indexJobs.FindActiveJobIdForRepoAsync(id, repositoryId, cancellationToken) is { } activeJobId)
            return Conflict(new { error = "index_in_progress", job_id = activeJobId });

        var (owner, repoName) = ParseOwnerRepo(repository.FullName);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repoName))
            return BadRequest(new { error = "invalid_repository_full_name" });

        string headSha;
        try
        {
            headSha = await _repoArchive.GetDefaultBranchHeadShaAsync(repository.InstallationId, owner, repoName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve default branch HEAD for repository {RepositoryId}.", repositoryId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "github_unavailable" });
        }

        // Full index: BaseSha null. Dedupe by repository + head so re-triggering the same SHA is a no-op.
        var dedupeKey = $"{repositoryId:D}:full:{headSha}";
        var enqueue = await _indexJobs.TryCreateQueuedJobAsync(
            id,
            repositoryId,
            repository.InstallationId,
            owner,
            repoName,
            headSha,
            baseSha: null,
            dedupeKey,
            cancellationToken);

        if (!enqueue.Created && !enqueue.NeedsRepublish)
        {
            if (string.Equals(enqueue.BlockReason, "suspended", StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "tenant_suspended" });

            if (string.Equals(enqueue.BlockReason, "duplicate", StringComparison.OrdinalIgnoreCase))
                return Accepted(new { already_indexed = true, head_sha = headSha, job_id = enqueue.JobId });

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "enqueue_failed" });
        }

        var jobMessage = new RepoIndexJobMessage(
            id,
            enqueue.JobId!.Value,
            repositoryId,
            repository.InstallationId,
            owner,
            repoName,
            headSha,
            BaseSha: null,
            Attempt: 0);

        try
        {
            await _indexPublisher.PublishAsync(jobMessage, cancellationToken);
            await _indexJobs.MarkJobQueuedAfterPublishAsync(id, enqueue.JobId.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish manual repo index job {JobId}; remains PendingPublish for reconciler.", enqueue.JobId);
        }

        return Accepted(new { job_id = enqueue.JobId });
    }

    private static (string Owner, string Repo) ParseOwnerRepo(string fullName)
    {
        var parts = fullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (string.Empty, string.Empty);
    }

    [HttpGet("usage")]
    public async Task<ActionResult<TenantUsageResponse>> Usage(
        Guid id,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var summary = await _jobs.GetTenantUsageSummaryAsync(id, days, cancellationToken);
        if (summary is null)
            return NotFound();

        return Ok(new TenantUsageResponse(
            summary.Days,
            summary.CompletedReviews,
            summary.TotalInputTokens,
            summary.TotalOutputTokens,
            summary.TotalEstimatedCostZar));
    }

    [HttpGet("audit")]
    public async Task<ActionResult<AuditLogPageResponse>> Audit(
        Guid id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var (items, total) = await _jobs.ListAuditReviewsAsync(id, skip, take, cancellationToken);
        return Ok(new AuditLogPageResponse(
            total,
            items.Select(e => new AuditLogRowResponse(
                e.Timestamp,
                e.PrNumber,
                e.RepositoryFullName,
                e.FindingsCount,
                e.DurationMs,
                e.EstimatedCostZar,
                e.InputTokens,
                e.OutputTokens)).ToList()));
    }

    [HttpGet("findings")]
    public async Task<ActionResult<FindingsPageResponse>> Findings(
        Guid id,
        [FromQuery] string? severity,
        [FromQuery] string? file,
        [FromQuery] string? rule_id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var q = new FindingListQuery(severity, file, rule_id, skip, take);
        var (items, total) = await _findings.ListForTenantAsync(id, q, cancellationToken);
        return Ok(new FindingsPageResponse(
            total,
            items.Select(MapFinding).ToList()));
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<TenantPreferencesResponse>> GetPreferences(Guid id, CancellationToken cancellationToken)
    {
        var prefs = await _tenants.GetPortalPreferencesAsync(id, cancellationToken);
        if (prefs is null)
            return NotFound();
        return Ok(MapPreferences(prefs));
    }

    [HttpPatch("preferences")]
    public async Task<ActionResult<TenantPreferencesResponse>> PatchPreferences(
        Guid id,
        [FromBody] TenantPreferencesPatchRequest body,
        CancellationToken cancellationToken)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var patch = new TenantPortalPreferencesPatch(
            body.ReviewAllPullRequests,
            body.SkipDraftPullRequests,
            body.EnforceCursorRules,
            body.MonetaryTypeSafetyChecks,
            body.EmailOnCriticalIssues,
            body.WeeklyDigest);

        var updated = await _tenants.UpdatePortalPreferencesAsync(id, patch, cancellationToken);
        if (updated is null)
            return NotFound();

        return Ok(MapPreferences(updated));
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationsListResponse>> Notifications(
        Guid id,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var items = await _jobs.ListPortalNotificationsAsync(id, take, cancellationToken);
        return Ok(new NotificationsListResponse(
            items.Select(n => new NotificationRowResponse(
                n.Kind,
                n.Title,
                n.Subtitle,
                n.OccurredAt,
                n.JobId)).ToList()));
    }

    [HttpGet("config")]
    [Produces("application/json")]
    public async Task<ActionResult<TenantConfigGetResponse>> GetConfig(Guid id, CancellationToken cancellationToken)
    {
        if (await _tenants.GetAccountSnapshotAsync(id, cancellationToken) is null)
            return NotFound();

        var yaml = await _tenants.GetConfigYamlAsync(id, cancellationToken);
        return Ok(new TenantConfigGetResponse(yaml ?? string.Empty));
    }

    [HttpPost("config")]
    public async Task<ActionResult<ConfigValidationResponse>> PostConfig(Guid id, CancellationToken cancellationToken)
    {
        string yaml;
        using (var reader = new StreamReader(Request.Body, leaveOpen: false))
            yaml = await reader.ReadToEndAsync(cancellationToken);

        var (valid, errors) = _yamlValidator.Validate(yaml);
        if (!valid)
            return BadRequest(new ConfigValidationResponse(false, errors));

        await _tenants.UpdateConfigYamlAsync(id, yaml, cancellationToken);
        return Ok(new ConfigValidationResponse(true, Array.Empty<string>()));
    }

    public sealed record TenantJobRowResponse(
        [property: JsonPropertyName("job_id")] Guid JobId,
        [property: JsonPropertyName("repository_full_name")] string RepositoryFullName,
        [property: JsonPropertyName("pr_number")] int PrNumber,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
        [property: JsonPropertyName("duration_ms")] long? DurationMs,
        [property: JsonPropertyName("findings_count")] int FindingsCount,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("estimated_cost_zar")] decimal? EstimatedCostZar);

    public sealed record TenantJobsPageResponse(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("items")] IReadOnlyList<TenantJobRowResponse> Items);

    public sealed record TenantRepositoryRowResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("job_count")] int JobCount);

    public sealed record TenantUsageResponse(
        [property: JsonPropertyName("days")] int Days,
        [property: JsonPropertyName("completed_reviews")] int CompletedReviews,
        [property: JsonPropertyName("total_input_tokens")] long TotalInputTokens,
        [property: JsonPropertyName("total_output_tokens")] long TotalOutputTokens,
        [property: JsonPropertyName("total_estimated_cost_zar")] decimal TotalEstimatedCostZar);

    private static FindingRowResponse MapFinding(FindingReadDto f) =>
        new(
            f.Id,
            f.Severity,
            f.Category,
            f.RuleId,
            f.Source,
            f.FilePath,
            f.LineNumber,
            f.Message,
            f.WasActioned,
            f.PrMergeStatus,
            f.CreatedAt);

    private static TenantPreferencesResponse MapPreferences(TenantPortalPreferences p) =>
        new(
            p.ReviewAllPullRequests,
            p.SkipDraftPullRequests,
            p.EnforceCursorRules,
            p.MonetaryTypeSafetyChecks,
            p.EmailOnCriticalIssues,
            p.WeeklyDigest);

    private static TenantJobRowResponse MapJobRow(TenantPrJobRow r) =>
        new(
            r.JobId,
            r.RepositoryFullName,
            r.PrNumber,
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.DurationMs,
            r.FindingsCount,
            r.InputTokens,
            r.OutputTokens,
            r.EstimatedCostZar);

    public sealed record TenantStatusResponse(
        [property: JsonPropertyName("onboarding_status")] string OnboardingStatus,
        [property: JsonPropertyName("repositories_detected")] int RepositoriesDetected,
        [property: JsonPropertyName("first_pr_reviewed")] bool FirstPrReviewed);

    public sealed record TenantInstallUrlResponse(
        [property: JsonPropertyName("install_url")] string InstallUrl,
        [property: JsonPropertyName("github_org")] string? GitHubOrg);

    public sealed record TenantAccountResponse(
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("company_name")] string CompanyName,
        [property: JsonPropertyName("plan")] string Plan,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("contact_email")] string? ContactEmail,
        [property: JsonPropertyName("github_org")] string? GitHubOrg,
        [property: JsonPropertyName("github_app_connected")] bool GitHubAppConnected,
        [property: JsonPropertyName("github_app_installation_id")] long? GitHubAppInstallationId,
        [property: JsonPropertyName("has_config_yaml")] bool HasConfigYaml);

    public sealed record DailyCountResponse(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("count")] int Count);

    public sealed record TenantAnalyticsResponse(
        [property: JsonPropertyName("days")] int Days,
        [property: JsonPropertyName("prs_reviewed_in_period")] int PrsReviewedInPeriod,
        [property: JsonPropertyName("violations_in_period")] int ViolationsInPeriod,
        [property: JsonPropertyName("prs_reviewed_per_day")] IReadOnlyList<DailyCountResponse> PrsReviewedPerDay,
        [property: JsonPropertyName("violations_per_day")] IReadOnlyList<DailyCountResponse> ViolationsPerDay,
        [property: JsonPropertyName("architecture_drift_score")] int ArchitectureDriftScore,
        [property: JsonPropertyName("architecture_drift_note")] string ArchitectureDriftNote);

    public sealed record FindingRowResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("rule_id")] string? RuleId,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("file_path")] string FilePath,
        [property: JsonPropertyName("line_number")] int? LineNumber,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("was_actioned")] bool WasActioned,
        [property: JsonPropertyName("pr_merge_status")] string PrMergeStatus,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    public sealed record FindingsPageResponse(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("items")] IReadOnlyList<FindingRowResponse> Items);

    public sealed record FindingsListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<FindingRowResponse> Items);

    public sealed record AuditLogRowResponse(
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("pr_number")] int PrNumber,
        [property: JsonPropertyName("repository_full_name")] string RepositoryFullName,
        [property: JsonPropertyName("findings_count")] int FindingsCount,
        [property: JsonPropertyName("review_duration_ms")] long? ReviewDurationMs,
        [property: JsonPropertyName("estimated_cost_zar")] decimal? EstimatedCostZar,
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);

    public sealed record AuditLogPageResponse(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("items")] IReadOnlyList<AuditLogRowResponse> Items);

    public sealed record TenantConfigGetResponse(
        [property: JsonPropertyName("config_yaml")] string ConfigYaml);

    public sealed record TenantPreferencesResponse(
        [property: JsonPropertyName("review_all_pull_requests")] bool ReviewAllPullRequests,
        [property: JsonPropertyName("skip_draft_pull_requests")] bool SkipDraftPullRequests,
        [property: JsonPropertyName("enforce_cursorrules")] bool EnforceCursorRules,
        [property: JsonPropertyName("monetary_type_safety_checks")] bool MonetaryTypeSafetyChecks,
        [property: JsonPropertyName("email_on_critical_issues")] bool EmailOnCriticalIssues,
        [property: JsonPropertyName("weekly_digest")] bool WeeklyDigest);

    public sealed class TenantPreferencesPatchRequest
    {
        [JsonPropertyName("review_all_pull_requests")]
        public bool? ReviewAllPullRequests { get; set; }

        [JsonPropertyName("skip_draft_pull_requests")]
        public bool? SkipDraftPullRequests { get; set; }

        [JsonPropertyName("enforce_cursorrules")]
        public bool? EnforceCursorRules { get; set; }

        [JsonPropertyName("monetary_type_safety_checks")]
        public bool? MonetaryTypeSafetyChecks { get; set; }

        [JsonPropertyName("email_on_critical_issues")]
        public bool? EmailOnCriticalIssues { get; set; }

        [JsonPropertyName("weekly_digest")]
        public bool? WeeklyDigest { get; set; }
    }

    public sealed record NotificationRowResponse(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("subtitle")] string Subtitle,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("job_id")] Guid? JobId);

    public sealed record NotificationsListResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<NotificationRowResponse> Items);

    public sealed record ConfigValidationResponse(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

    public sealed record TenantRotateKeyResponse([property: JsonPropertyName("api_key")] string ApiKey);

    public sealed record TenantBillingResponse(
        [property: JsonPropertyName("plan")] string Plan,
        [property: JsonPropertyName("billing_status")] string BillingStatus,
        [property: JsonPropertyName("trial_ends_at")] DateTimeOffset? TrialEndsAt,
        [property: JsonPropertyName("paystack_customer_code")] string? PaystackCustomerCode,
        [property: JsonPropertyName("paystack_subscription_code")] string? PaystackSubscriptionCode,
        [property: JsonPropertyName("paystack_required")] bool PaystackRequired);

    public sealed class BillingSubscribeRequest
    {
        [JsonPropertyName("plan")]
        public string Plan { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string CallbackUrl { get; set; } = string.Empty;
    }

    public sealed record BillingSubscribeResponse(
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("authorization_url")] string AuthorizationUrl);

    public sealed class BillingConfirmRequest
    {
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }

    public sealed record BillingConfirmResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("billing_status")] string? BillingStatus,
        [property: JsonPropertyName("paystack_subscription_code")] string? PaystackSubscriptionCode,
        [property: JsonPropertyName("error")] string? Error);

    public sealed class BillingChangePlanRequest
    {
        [JsonPropertyName("plan")]
        public string Plan { get; set; } = string.Empty;
    }

    public sealed record BillingChangePlanResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("plan")] string? Plan,
        [property: JsonPropertyName("error")] string? Error);
}
