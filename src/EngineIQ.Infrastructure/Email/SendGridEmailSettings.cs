namespace EngineIQ.Infrastructure.Email;

public static class SendGridEmailSettings
{
    public static bool IsCriticalIssuesConfigured(SendGridOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.TemplateCriticalIssues);
}
