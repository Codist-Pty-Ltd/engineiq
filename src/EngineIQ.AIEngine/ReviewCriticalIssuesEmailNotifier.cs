using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Notifications;
using EngineIQ.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace EngineIQ.AIEngine;

/// <summary>
/// Sends optional SendGrid alert when a completed review has critical findings and the tenant opted in.
/// </summary>
public static class ReviewCriticalIssuesEmailNotifier
{
    public static async Task TryNotifyAsync(
        IEmailNotificationService email,
        ITenantRepository tenants,
        string dashboardBaseUrl,
        PullReviewJobMessage job,
        IReadOnlyList<FindingWriteDto> parsedFindings,
        TenantPortalPreferences preferences,
        bool sendGridConfigured,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!CriticalIssuesNotificationPolicy.ShouldNotify(parsedFindings, preferences, sendGridConfigured))
            return;

        var critical = CriticalIssuesNotificationPolicy.SelectCritical(parsedFindings);
        var account = await tenants.GetAccountSnapshotAsync(job.TenantId, cancellationToken);
        if (account is null || string.IsNullOrWhiteSpace(account.ContactEmail))
        {
            logger.LogInformation(
                "Critical-issues email skipped for tenant {TenantId}: no contact email on file.",
                job.TenantId);
            return;
        }

        var dashboard = dashboardBaseUrl.TrimEnd('/');
        var jobDetailUrl =
            $"{dashboard}/dashboard/reviews?job={Uri.EscapeDataString(job.JobId.ToString("D"))}";
        var repositoryFullName = $"{job.Owner}/{job.Repo}";

        try
        {
            await email.SendCriticalIssuesNotificationAsync(
                new CriticalIssuesEmailRequest(
                    account.ContactEmail.Trim(),
                    account.CompanyName,
                    repositoryFullName,
                    job.PrNumber,
                    critical.Count,
                    critical.Select(f => f.Message).ToList(),
                    jobDetailUrl),
                cancellationToken);

            logger.LogInformation(
                "Sent critical-issues email to tenant {TenantId} for job {JobId} ({Count} findings).",
                job.TenantId,
                job.JobId,
                critical.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to send critical-issues email for tenant {TenantId} job {JobId}; job completion continues.",
                job.TenantId,
                job.JobId);
        }
    }
}
