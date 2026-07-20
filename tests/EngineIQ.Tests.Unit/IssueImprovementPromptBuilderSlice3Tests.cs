using EngineIQ.AIEngine.IssueImprovement;
using EngineIQ.Domain.Jira;

namespace EngineIQ.Tests.Unit;

public class IssueImprovementPromptBuilderSlice3Tests
{
    private static readonly JiraIssueDetails SampleIssue = new(
        "ENG-1",
        1001,
        "Bug",
        "Add login",
        "Need SSO",
        "High",
        "alice",
        "ENG",
        DateTimeOffset.UtcNow);

    [Fact]
    public void With_parent_includes_parent_epic_section()
    {
        var parent = new JiraParentSummary("ENG-100", "Auth epic", "SSO for all apps");
        var user = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, null, null, parent);

        Assert.Contains("## Parent epic", user);
        Assert.Contains("Key: ENG-100", user);
        Assert.Contains("Summary: Auth epic", user);
        Assert.Contains("SSO for all apps", user);
    }

    [Fact]
    public void Without_parent_is_byte_identical_to_slice2b()
    {
        var slice2b = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, null, null);
        var slice3 = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, null, null, parent: null);
        Assert.Equal(slice2b, slice3);
        Assert.DoesNotContain("## Parent epic", slice3);
    }
}
