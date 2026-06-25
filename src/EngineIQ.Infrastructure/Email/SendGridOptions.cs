namespace EngineIQ.Infrastructure.Email;

public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    /// <summary>API key from environment (e.g. SENDGRID_API_KEY) — never log this value.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string FromEmail { get; set; } = "noreply@engineiq.io";
    public string FromName { get; set; } = "EngineIQ";

    public string TemplateWelcome { get; set; } = string.Empty;
    public string TemplateLiveConfirmation { get; set; } = string.Empty;
    public string TemplateThirtyDayReport { get; set; } = string.Empty;
    public string TemplateCriticalIssues { get; set; } = string.Empty;
}
