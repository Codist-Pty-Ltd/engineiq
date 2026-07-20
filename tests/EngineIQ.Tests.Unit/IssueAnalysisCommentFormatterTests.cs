using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
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

        var analyzedAt = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var body = IssueAnalysisCommentFormatter.Format(
            result, "\n\n----\nfooter", AnalysisTrigger.Created, analyzedAt);

        Assert.Contains("_EngineIQ analysis — new issue — updated 2026-07-20_", body);
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

        var analyzedAt = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var body = IssueAnalysisCommentFormatter.Format(
            result, "\nfooter", AnalysisTrigger.Created, analyzedAt);
        Assert.Contains("_EngineIQ analysis — new issue — updated 2026-07-19_", body);
        Assert.Contains("h2. EngineIQ review", body);
        Assert.Contains("well-formed", body);
        Assert.Contains("footer", body);
    }
}
