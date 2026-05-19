using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Infrastructure.Persistence;

internal static class PortalPreferencesJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TenantPortalPreferences Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new TenantPortalPreferences();

        try
        {
            return JsonSerializer.Deserialize<TenantPortalPreferencesDto>(json, Options)?.ToDomain()
                   ?? new TenantPortalPreferences();
        }
        catch
        {
            return new TenantPortalPreferences();
        }
    }

    public static string Serialize(TenantPortalPreferences prefs) =>
        JsonSerializer.Serialize(TenantPortalPreferencesDto.FromDomain(prefs), Options);

    public static TenantPortalPreferences Merge(TenantPortalPreferences current, TenantPortalPreferencesPatch patch) =>
        new(
            patch.ReviewAllPullRequests ?? current.ReviewAllPullRequests,
            patch.SkipDraftPullRequests ?? current.SkipDraftPullRequests,
            patch.EnforceCursorRules ?? current.EnforceCursorRules,
            patch.MonetaryTypeSafetyChecks ?? current.MonetaryTypeSafetyChecks,
            patch.EmailOnCriticalIssues ?? current.EmailOnCriticalIssues,
            patch.WeeklyDigest ?? current.WeeklyDigest);

    private sealed class TenantPortalPreferencesDto
    {
        [JsonPropertyName("review_all_pull_requests")]
        public bool ReviewAllPullRequests { get; set; } = true;

        [JsonPropertyName("skip_draft_pull_requests")]
        public bool SkipDraftPullRequests { get; set; } = true;

        [JsonPropertyName("enforce_cursorrules")]
        public bool EnforceCursorRules { get; set; }

        [JsonPropertyName("monetary_type_safety_checks")]
        public bool MonetaryTypeSafetyChecks { get; set; } = true;

        [JsonPropertyName("email_on_critical_issues")]
        public bool EmailOnCriticalIssues { get; set; } = true;

        [JsonPropertyName("weekly_digest")]
        public bool WeeklyDigest { get; set; }

        public TenantPortalPreferences ToDomain() =>
            new(
                ReviewAllPullRequests,
                SkipDraftPullRequests,
                EnforceCursorRules,
                MonetaryTypeSafetyChecks,
                EmailOnCriticalIssues,
                WeeklyDigest);

        public static TenantPortalPreferencesDto FromDomain(TenantPortalPreferences p) =>
            new()
            {
                ReviewAllPullRequests = p.ReviewAllPullRequests,
                SkipDraftPullRequests = p.SkipDraftPullRequests,
                EnforceCursorRules = p.EnforceCursorRules,
                MonetaryTypeSafetyChecks = p.MonetaryTypeSafetyChecks,
                EmailOnCriticalIssues = p.EmailOnCriticalIssues,
                WeeklyDigest = p.WeeklyDigest,
            };
    }
}
