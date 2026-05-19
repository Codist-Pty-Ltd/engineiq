using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Tests.Unit;

public class ReviewEnqueuePolicyTests
{
    [Fact]
    public void ShouldEnqueue_false_when_auto_review_disabled()
    {
        var prefs = new TenantPortalPreferences(ReviewAllPullRequests: false);
        var ok = ReviewEnqueuePolicy.ShouldEnqueue(prefs, isDraft: false, out var reason);
        Assert.False(ok);
        Assert.Equal("auto_review_disabled", reason);
    }

    [Fact]
    public void ShouldEnqueue_false_when_skip_drafts_and_pr_is_draft()
    {
        var prefs = new TenantPortalPreferences(SkipDraftPullRequests: true);
        var ok = ReviewEnqueuePolicy.ShouldEnqueue(prefs, isDraft: true, out var reason);
        Assert.False(ok);
        Assert.Equal("draft_pr", reason);
    }

    [Fact]
    public void ShouldEnqueue_true_for_open_pr_with_defaults()
    {
        var ok = ReviewEnqueuePolicy.ShouldEnqueue(new TenantPortalPreferences(), isDraft: false, out var reason);
        Assert.True(ok);
        Assert.Null(reason);
    }
}
