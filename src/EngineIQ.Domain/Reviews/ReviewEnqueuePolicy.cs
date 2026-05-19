using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Reviews;

/// <summary>Decides whether an incoming PR event should enqueue an automated review.</summary>
public static class ReviewEnqueuePolicy
{
    public static bool ShouldEnqueue(TenantPortalPreferences preferences, bool isDraft, out string? skipReason)
    {
        if (!preferences.ReviewAllPullRequests)
        {
            skipReason = "auto_review_disabled";
            return false;
        }

        if (preferences.SkipDraftPullRequests && isDraft)
        {
            skipReason = "draft_pr";
            return false;
        }

        skipReason = null;
        return true;
    }
}
