using EngineIQ.AIEngine.IssueImprovement;
using EngineIQ.Domain.Jira;

namespace EngineIQ.Tests.Unit;

public class IssueImprovementPromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_mentions_issue_type_and_json_schema()
    {
        var prompt = IssueImprovementPromptBuilder.BuildSystemPrompt("Bug");
        Assert.Contains("Bug", prompt);
        Assert.Contains("rewrittenDescription", prompt);
        Assert.Contains("isAlreadyWellFormed", prompt);
    }

    [Fact]
    public void BuildUserPrompt_includes_key_fields()
    {
        var issue = new JiraIssueDetails(
            "ENG-1",
            1001,
            "Story",
            "Add login",
            "Need SSO",
            "High",
            "alice",
            "ENG",
            DateTimeOffset.UtcNow);

        var prompt = IssueImprovementPromptBuilder.BuildUserPrompt(issue);
        Assert.Contains("ENG-1", prompt);
        Assert.Contains("Story", prompt);
        Assert.Contains("Add login", prompt);
        Assert.Contains("Need SSO", prompt);
        Assert.Contains("alice", prompt);
    }

    [Fact]
    public void BuildUserPrompt_truncates_long_description()
    {
        var longDesc = new string('x', IssueImprovementPromptBuilder.MaxDescriptionChars + 500);
        var issue = new JiraIssueDetails(
            "ENG-2",
            1002,
            "Bug",
            "Summary",
            longDesc,
            null,
            null,
            "ENG",
            null);

        var prompt = IssueImprovementPromptBuilder.BuildUserPrompt(issue);
        Assert.Contains($"truncated to {IssueImprovementPromptBuilder.MaxDescriptionChars}", prompt);
        Assert.DoesNotContain(longDesc, prompt);
    }
}
