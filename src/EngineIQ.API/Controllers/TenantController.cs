using System.Text.Json.Serialization;
using EngineIQ.API.Validation;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jobs;
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
    private readonly IFindingRepository _findings;
    private readonly IJobRepository _jobs;
    private readonly StandardsConfigYamlValidator _yamlValidator;
    private readonly IOptions<GitHubClientOptions> _gitHub;
    private readonly ILogger<TenantController> _logger;

    public TenantController(
        ITenantRepository tenants,
        IFindingRepository findings,
        IJobRepository jobs,
        StandardsConfigYamlValidator yamlValidator,
        IOptions<GitHubClientOptions> gitHub,
        ILogger<TenantController> logger)
    {
        _tenants = tenants;
        _findings = findings;
        _jobs = jobs;
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
}
