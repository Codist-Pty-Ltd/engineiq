using EngineIQ.AIEngine.IssueImprovement;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;

namespace EngineIQ.Tests.Unit;

public class IssueImprovementPromptBuilderSlice2bTests
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
    public void Without_hits_system_and_user_prompts_are_byte_identical_to_slice1()
    {
        var systemSlice1 = IssueImprovementPromptBuilder.BuildSystemPrompt("Bug");
        var systemNoCtx = IssueImprovementPromptBuilder.BuildSystemPrompt("Bug", hasCodeContext: false);
        Assert.Equal(systemSlice1, systemNoCtx);
        Assert.DoesNotContain("impactAnalysis", systemSlice1);

        var userSlice1 = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue);
        var userNoCtx = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, null, null);
        Assert.Equal(userSlice1, userNoCtx);
        Assert.DoesNotContain("## Relevant code", userSlice1);
    }

    [Fact]
    public void With_hits_prompt_includes_code_sections_and_paths_verbatim()
    {
        var hits = new CodeSearchResult(
            new[]
            {
                new CodeSearchHit(
                    Guid.NewGuid(),
                    "codist/mybillable",
                    "src/InvoiceService.cs",
                    "Sample.InvoiceService.Save",
                    10,
                    40,
                    "public void Save() {}",
                    0.9),
            },
            1);

        var repoContext = new RepoContext(
            "Clean Architecture",
            new Dictionary<string, List<string>> { ["Domain"] = new() { "src/Domain" } },
            new List<string>(),
            DateTimeOffset.UtcNow);

        var system = IssueImprovementPromptBuilder.BuildSystemPrompt("Bug", hasCodeContext: true);
        Assert.Contains("impactAnalysis", system);
        Assert.Contains("never invent file paths", system);

        var user = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, hits, repoContext);
        Assert.Contains("## Repository architecture", user);
        Assert.Contains("Clean Architecture", user);
        Assert.Contains("## Relevant code from the indexed codebase", user);
        Assert.Contains("// codist/mybillable/src/InvoiceService.cs [Sample.InvoiceService.Save] lines 10-40", user);
        Assert.Contains("public void Save() {}", user);
    }

    [Fact]
    public void With_hits_but_null_repo_context_omits_architecture_section()
    {
        var hits = new CodeSearchResult(
            new[]
            {
                new CodeSearchHit(Guid.NewGuid(), "org/repo", "a.cs", null, 1, 2, "x", 1),
            },
            1);

        var user = IssueImprovementPromptBuilder.BuildUserPrompt(SampleIssue, hits, null);
        Assert.DoesNotContain("## Repository architecture", user);
        Assert.Contains("## Relevant code from the indexed codebase", user);
    }
}
