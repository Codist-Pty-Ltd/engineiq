using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Notifications;

public static class CriticalIssuesNotificationPolicy
{
    public static bool ShouldNotify(
        IReadOnlyList<FindingWriteDto> findings,
        TenantPortalPreferences preferences,
        bool sendGridConfigured) =>
        sendGridConfigured
        && preferences.EmailOnCriticalIssues
        && findings.Any(IsCritical);

    public static IReadOnlyList<FindingWriteDto> SelectCritical(IReadOnlyList<FindingWriteDto> findings) =>
        findings.Where(IsCritical).ToList();

    public static bool IsCritical(FindingWriteDto finding) =>
        string.Equals(finding.Severity, "critical", StringComparison.OrdinalIgnoreCase);
}
