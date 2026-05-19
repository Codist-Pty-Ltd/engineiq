namespace EngineIQ.Domain.Tenants;

/// <summary>Tenant-controlled portal settings (review behaviour and email notification opt-ins).</summary>
public sealed record TenantPortalPreferences(
    bool ReviewAllPullRequests = true,
    bool SkipDraftPullRequests = true,
    bool EnforceCursorRules = false,
    bool MonetaryTypeSafetyChecks = true,
    bool EmailOnCriticalIssues = true,
    bool WeeklyDigest = false);

public sealed record TenantPortalPreferencesPatch(
    bool? ReviewAllPullRequests = null,
    bool? SkipDraftPullRequests = null,
    bool? EnforceCursorRules = null,
    bool? MonetaryTypeSafetyChecks = null,
    bool? EmailOnCriticalIssues = null,
    bool? WeeklyDigest = null);

public sealed record PortalNotificationItem(
    string Kind,
    string Title,
    string Subtitle,
    DateTimeOffset OccurredAt,
    Guid? JobId);
