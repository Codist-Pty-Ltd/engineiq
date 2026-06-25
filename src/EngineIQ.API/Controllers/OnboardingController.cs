using System.Text.Json.Serialization;
using EngineIQ.API.Options;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Tenants;
using EngineIQ.Domain.Trust;
using EngineIQ.GitHub;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EngineIQ.API.Controllers;

[ApiController]
[Route("api/v1/onboarding")]
[EnableRateLimiting("onboarding")]
[EnableCors("Portal")]
public sealed class OnboardingController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantBillingService _billing;
    private readonly IEmailNotificationService _email;
    private readonly IOptions<GitHubClientOptions> _gitHub;
    private readonly IOptions<EngineIQAppOptions> _app;
    private readonly IOptions<TrustOptions> _trust;
    private readonly ILogger<OnboardingController> _logger;

    public OnboardingController(
        ITenantRepository tenants,
        ITenantBillingService billing,
        IEmailNotificationService email,
        IOptions<GitHubClientOptions> gitHub,
        IOptions<EngineIQAppOptions> app,
        IOptions<TrustOptions> trust,
        ILogger<OnboardingController> logger)
    {
        _tenants = tenants;
        _billing = billing;
        _email = email;
        _gitHub = gitHub;
        _app = app;
        _trust = trust;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_gitHub.Value.AppSlug))
        {
            _logger.LogError("GitHub:AppSlug is not configured; cannot build install URL.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "server_misconfigured" });
        }

        var validation = ValidateRegister(body);
        if (validation is { Length: > 0 })
            return BadRequest(new { errors = validation });

        if (!body.DpaAccepted)
            return BadRequest(new { errors = new[] { "dpa_accepted_required" } });

        var cmd = new RegisterTenantCommand(
            body.Email.Trim(),
            body.CompanyName.Trim(),
            body.Plan.Trim(),
            body.GithubOrg.Trim(),
            GetClientIp(HttpContext));

        RegisterTenantResult created;
        try
        {
            created = await _tenants.RegisterAsync(cmd, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant registration failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "registration_failed" });
        }

        try
        {
            await _billing.ProvisionCustomerAfterRegisterAsync(
                created.TenantId,
                cmd.Email,
                cmd.CompanyName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Paystack customer provisioning failed; tenant {TenantId} remains in Trialing.",
                created.TenantId);
        }

        var slug = _gitHub.Value.AppSlug.Trim();
        var installUrl =
            $"https://github.com/apps/{slug}/installations/new?state={Uri.EscapeDataString(created.GithubInstallState)}";

        try
        {
            await _email.SendWelcomeWithInstallLinkAsync(
                cmd.Email,
                cmd.CompanyName,
                installUrl,
                _trust.Value.DpaPdfUrl.Trim(),
                created.WebhookSecretPlaintext,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Welcome email failed; tenant was still created.");
        }

        return Ok(new RegisterResponse(created.TenantId, installUrl, created.ApiKey));
    }

    [AcceptVerbs("GET", "POST")]
    [Route("github-callback")]
    public async Task<IActionResult> GitHubCallback(CancellationToken cancellationToken)
    {
        if (!TryReadInstallationId(Request, out var installationId, out var installState, out var setupAction))
            return BadRequest(new { error = "missing_parameters" });

        if (setupAction is not null && !string.Equals(setupAction, "install", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "unsupported_setup_action" });

        var (ok, tenantId, contactEmail, error) = await _tenants.CompleteGitHubInstallAsync(installationId, installState, cancellationToken);
        if (!ok)
            return BadRequest(new { error });

        var dashboard = _app.Value.DashboardBaseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(contactEmail))
        {
            try
            {
                await _email.SendLiveConfirmationAsync(contactEmail.Trim(), $"{dashboard}/", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live confirmation email failed.");
            }
        }

        return Redirect($"{dashboard}/?onboarding=complete&tenant_id={tenantId:N}");
    }

    private static bool TryReadInstallationId(
        HttpRequest request,
        out long installationId,
        out string state,
        out string? setupAction)
    {
        installationId = 0;
        state = string.Empty;
        setupAction = null;

        string? inst = request.Query["installation_id"].FirstOrDefault() ?? request.Form["installation_id"].FirstOrDefault();
        state = request.Query["state"].FirstOrDefault() ?? request.Form["state"].FirstOrDefault() ?? string.Empty;
        setupAction = request.Query["setup_action"].FirstOrDefault() ?? request.Form["setup_action"].FirstOrDefault();

        if (string.IsNullOrEmpty(inst) || !long.TryParse(inst, out installationId))
            return false;
        return !string.IsNullOrWhiteSpace(state);
    }

    private static string[] ValidateRegister(RegisterRequest body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(body.Email) || body.Email.Length > 320)
            errors.Add("email_invalid");
        else if (!body.Email.Contains('@'))
            errors.Add("email_invalid");

        if (string.IsNullOrWhiteSpace(body.CompanyName) || body.CompanyName.Length > 256)
            errors.Add("company_name_invalid");

        if (string.IsNullOrWhiteSpace(body.Plan) || body.Plan.Length > 64)
            errors.Add("plan_invalid");

        if (string.IsNullOrWhiteSpace(body.GithubOrg) || body.GithubOrg.Length > 39)
            errors.Add("github_org_invalid");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(body.GithubOrg.Trim(), "^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$"))
            errors.Add("github_org_invalid");

        return errors.ToArray();
    }

    public sealed class RegisterRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("company_name")]
        public string CompanyName { get; set; } = string.Empty;

        [JsonPropertyName("plan")]
        public string Plan { get; set; } = string.Empty;

        [JsonPropertyName("github_org")]
        public string GithubOrg { get; set; } = string.Empty;

        [JsonPropertyName("dpa_accepted")]
        public bool DpaAccepted { get; set; }
    }

    public sealed record RegisterResponse(
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("install_url")] string InstallUrl,
        [property: JsonPropertyName("api_key")] string ApiKey);

    private static string? GetClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0)
                return first;
        }

        return http.Connection.RemoteIpAddress?.ToString();
    }
}
