using EngineIQ.Domain.Jira;
using EngineIQ.FeedbackGenerator;

namespace EngineIQ.Tests.Unit;

public class IssueAnalysisCommentFormatterSlice2bTests
{
    [Fact]
    public void Format_renders_impact_analysis_section_in_jira_wiki_markup()
    {
        var result = new IssueImprovementResult(
            "Improved body",
            new[] { "Given A When B Then C" },
            new[] { "What is the environment?" },
            "High — data loss risk",
            IsAlreadyWellFormed: false,
            ImpactAnalysis: new IssueImpactAnalysis(
                new[] { new ImpactedFile("src/InvoiceService.cs", "owns the save path", "High") },
                new[] { "Billing", "API" },
                "InvoiceServiceTests and migration 014",
                new[] { "Open InvoiceService.Save", "Add null guard", "Extend unit tests" }));

        var body = IssueAnalysisCommentFormatter.Format(result, "\n\n----\nfooter");

        Assert.Contains("h3. Acceptance criteria", body);
        Assert.Contains("h3. Impact Analysis", body);
        Assert.Contains("*Likely files:*", body);
        Assert.Contains("- src/InvoiceService.cs — owns the save path _(High confidence)_", body);
        Assert.Contains("*Affected modules:* Billing, API", body);
        Assert.Contains("*Blast radius:* InvoiceServiceTests and migration 014", body);
        Assert.Contains("*Suggested approach:*", body);
        Assert.Contains("# Open InvoiceService.Save", body);
        Assert.Contains("footer", body);

        // Impact appears after acceptance criteria.
        Assert.True(body.IndexOf("Acceptance criteria", StringComparison.Ordinal) <
                    body.IndexOf("Impact Analysis", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_omits_impact_section_when_null()
    {
        var result = new IssueImprovementResult(
            "Improved body",
            new[] { "ac" },
            Array.Empty<string>(),
            "Low",
            IsAlreadyWellFormed: false);

        var body = IssueAnalysisCommentFormatter.Format(result, "\nfooter");
        Assert.DoesNotContain("Impact Analysis", body);
    }
}
