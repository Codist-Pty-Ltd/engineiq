using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.FeedbackGenerator;

namespace EngineIQ.Tests.Unit;

public class ReviewCommentFormatterTests
{
    [Fact]
    public void Format_groups_by_severity_and_labels_rule_id()
    {
        var findings = new[]
        {
            Finding("critical", "SEC-001", FindingSources.Rule, "DbConfig.cs", 3, "Hardcoded secret."),
            Finding("medium", null, FindingSources.AI, "Order.cs", 8, "Naming nit."),
        };

        var body = ReviewCommentFormatter.Format(findings, "\n\n---\nfooter");

        Assert.Contains("### Critical", body);
        Assert.Contains("**[SEC-001 | Rule]**", body);
        Assert.Contains("### Medium", body);
        Assert.Contains("**[general | AI]**", body);
        Assert.Contains("footer", body);
    }

    private static FindingWriteDto Finding(
        string severity,
        string? ruleId,
        string source,
        string path,
        int line,
        string message) =>
        new(
            severity,
            ruleId is null ? "general" : "security",
            ruleId,
            source,
            path,
            line,
            message,
            false,
            "unknown",
            null);
}
