using EngineIQ.Domain.Jira;
using EngineIQ.FeedbackGenerator;

namespace EngineIQ.Tests.Unit;

public class IssueAnalysisCommentFormatterTests
{
    [Fact]
    public void Format_includes_sections_and_trust_footer()
    {
        var result = new IssueImprovementResult(
            "Improved body",
            new[] { "Given A When B Then C" },
            new[] { "What is the environment?" },
            "High — data loss risk",
            IsAlreadyWellFormed: false);

        var body = IssueAnalysisCommentFormatter.Format(result, "\n\n----\nfooter");

        Assert.Contains("h2. EngineIQ ticket improvement", body);
        Assert.Contains("h3. Improved description", body);
        Assert.Contains("Improved body", body);
        Assert.Contains("* Given A When B Then C", body);
        Assert.Contains("* What is the environment?", body);
        Assert.Contains("h3. Severity", body);
        Assert.Contains("footer", body);
    }

    [Fact]
    public void Format_well_formed_uses_lighter_heading()
    {
        var result = new IssueImprovementResult(
            "",
            Array.Empty<string>(),
            Array.Empty<string>(),
            "Low",
            IsAlreadyWellFormed: true);

        var body = IssueAnalysisCommentFormatter.Format(result, "\nfooter");
        Assert.Contains("h2. EngineIQ review", body);
        Assert.Contains("well-formed", body);
        Assert.Contains("footer", body);
    }
}
