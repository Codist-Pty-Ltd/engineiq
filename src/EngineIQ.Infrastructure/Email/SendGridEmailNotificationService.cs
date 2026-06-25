using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Infrastructure.Email;

public sealed class SendGridEmailNotificationService : IEmailNotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SendGridOptions _options;
    private readonly ILogger<SendGridEmailNotificationService> _logger;

    public SendGridEmailNotificationService(
        IHttpClientFactory httpClientFactory,
        IOptions<SendGridOptions> options,
        ILogger<SendGridEmailNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task SendWelcomeWithInstallLinkAsync(
        string toEmail,
        string companyName,
        string installUrl,
        string dpaPdfUrl,
        string webhookSecretPlaintext,
        CancellationToken cancellationToken = default) =>
        SendTemplateAsync(
            toEmail,
            _options.TemplateWelcome,
            new Dictionary<string, string?>
            {
                ["company_name"] = companyName,
                ["install_url"] = installUrl,
                ["dpa_pdf_url"] = dpaPdfUrl,
                ["webhook_secret"] = webhookSecretPlaintext
            },
            cancellationToken);

    public Task SendLiveConfirmationAsync(string toEmail, string dashboardUrl, CancellationToken cancellationToken = default) =>
        SendTemplateAsync(
            toEmail,
            _options.TemplateLiveConfirmation,
            new Dictionary<string, string?>
            {
                ["dashboard_url"] = dashboardUrl
            },
            cancellationToken);

    public Task SendThirtyDayReportAsync(string toEmail, object templateData, CancellationToken cancellationToken = default) =>
        SendTemplateDynamicAsync(toEmail, _options.TemplateThirtyDayReport, templateData, cancellationToken);

    public Task SendCriticalIssuesNotificationAsync(
        CriticalIssuesEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var findingMessages = request.FindingMessages.Count == 0
            ? string.Empty
            : string.Join("\n", request.FindingMessages.Select(m => $"• {m}"));

        return SendTemplateAsync(
            request.ToEmail,
            _options.TemplateCriticalIssues,
            new Dictionary<string, string?>
            {
                ["company_name"] = request.CompanyName,
                ["repository_full_name"] = request.RepositoryFullName,
                ["pr_number"] = request.PrNumber.ToString(),
                ["critical_count"] = request.CriticalCount.ToString(),
                ["finding_messages"] = findingMessages,
                ["job_detail_url"] = request.JobDetailUrl,
                ["job_url"] = request.JobDetailUrl
            },
            cancellationToken,
            skipLogMessage: "SendGrid critical-issues email skipped (template or API key not configured).");
    }

    private async Task SendTemplateAsync(
        string toEmail,
        string templateId,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken,
        string? skipLogMessage = null)
    {
        if (string.IsNullOrWhiteSpace(templateId) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogInformation(skipLogMessage ?? "SendGrid welcome/live email skipped (template or API key not configured).");
            return;
        }

        var client = _httpClientFactory.CreateClient("SendGrid");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new SendGridMailRequest(
            new SendGridFrom(_options.FromEmail, _options.FromName),
            new[]
            {
                new SendGridPersonalization(
                    new[] { new SendGridTo(toEmail) },
                    data.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            },
            templateId));

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "SendGrid returned {Status} for template {TemplateId}.",
                (int)response.StatusCode,
                templateId);
        }
    }

    private async Task SendTemplateDynamicAsync(
        string toEmail,
        string templateId,
        object templateData,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templateId) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogInformation("SendGrid report email skipped (template or API key not configured).");
            return;
        }

        var client = _httpClientFactory.CreateClient("SendGrid");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new SendGridMailRequestDynamic(
            new SendGridFrom(_options.FromEmail, _options.FromName),
            new[] { new SendGridPersonalizationDynamic(new[] { new SendGridTo(toEmail) }, templateData) },
            templateId));

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("SendGrid returned {Status} for report template.", (int)response.StatusCode);
    }

    private sealed record SendGridFrom([property: JsonPropertyName("email")] string Email, [property: JsonPropertyName("name")] string Name);

    private sealed record SendGridTo([property: JsonPropertyName("email")] string Email);

    private sealed record SendGridPersonalization(
        [property: JsonPropertyName("to")] SendGridTo[] To,
        [property: JsonPropertyName("dynamic_template_data")] IReadOnlyDictionary<string, object?> DynamicTemplateData);

    private sealed record SendGridPersonalizationDynamic(
        [property: JsonPropertyName("to")] SendGridTo[] To,
        [property: JsonPropertyName("dynamic_template_data")] object DynamicTemplateData);

    private sealed record SendGridMailRequest(
        [property: JsonPropertyName("from")] SendGridFrom From,
        [property: JsonPropertyName("personalizations")] SendGridPersonalization[] Personalizations,
        [property: JsonPropertyName("template_id")] string TemplateId);

    private sealed record SendGridMailRequestDynamic(
        [property: JsonPropertyName("from")] SendGridFrom From,
        [property: JsonPropertyName("personalizations")] SendGridPersonalizationDynamic[] Personalizations,
        [property: JsonPropertyName("template_id")] string TemplateId);
}
