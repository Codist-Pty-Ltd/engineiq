namespace EngineIQ.Domain.Interfaces;

/// <summary>SendGrid-backed templates (welcome, live-confirmation, 30-day-report). Never logs API keys.</summary>
public interface IEmailNotificationService
{
    Task SendWelcomeWithInstallLinkAsync(
        string toEmail,
        string companyName,
        string installUrl,
        string dpaPdfUrl,
        string webhookSecretPlaintext,
        CancellationToken cancellationToken = default);

    Task SendLiveConfirmationAsync(string toEmail, string dashboardUrl, CancellationToken cancellationToken = default);

    /// <summary>Reserved for scheduled reporting; template id configured via SendGrid options.</summary>
    Task SendThirtyDayReportAsync(string toEmail, object templateData, CancellationToken cancellationToken = default);

    Task SendCriticalIssuesNotificationAsync(
        CriticalIssuesEmailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CriticalIssuesEmailRequest(
    string ToEmail,
    string CompanyName,
    string RepositoryFullName,
    int PrNumber,
    int CriticalCount,
    IReadOnlyList<string> FindingMessages,
    string JobDetailUrl);
