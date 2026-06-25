using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.FeedbackGenerator;

namespace EngineIQ.Tests.Unit;

public class ReviewFindingsMergerTests
{
    [Fact]
    public void Merge_overlapping_rule_and_ai_at_same_location_keeps_rule_only()
    {
        var rule = RuleFinding(
            "SEC-001",
            "critical",
            "No hardcoded secrets or connection strings",
            "src/Acme.Infrastructure/DbConfig.cs",
            3,
            "Possible hardcoded secret.");

        var ai = AiFinding(
            "high",
            "security",
            "src/Acme.Infrastructure/DbConfig.cs",
            3,
            "Hardcoded database password in connection string.");

        var merged = ReviewFindingsMerger.Merge([rule], [ai]);

        var single = Assert.Single(merged);
        Assert.Equal(FindingSources.Rule, single.Source);
        Assert.Equal("SEC-001", single.RuleId);
    }

    [Fact]
    public void Merge_non_overlapping_rule_and_ai_findings_both_survive()
    {
        var rule = RuleFinding(
            "ARCH-002",
            "high",
            "No business logic in controllers",
            "src/Acme.API/Controllers/OrdersController.cs",
            15,
            "Business logic (if) in controller.");

        var ai = AiFinding(
            "medium",
            "general",
            "src/Acme.Domain/Order.cs",
            8,
            "Consider null-checking the customer reference.");

        var merged = ReviewFindingsMerger.Merge([rule], [ai]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.Source == FindingSources.Rule && f.RuleId == "ARCH-002");
        Assert.Contains(merged, f => f.Source == FindingSources.AI);
    }

    [Fact]
    public void Merge_same_file_and_line_but_different_category_keeps_both()
    {
        var rule = RuleFinding(
            "PERF-001",
            "high",
            "No synchronous database calls in async context",
            "src/Acme.Infrastructure/Repo.cs",
            9,
            "Blocking async call pattern.");

        var ai = AiFinding(
            "medium",
            "general",
            "src/Acme.Infrastructure/Repo.cs",
            9,
            "Rename variable for clarity.");

        var merged = ReviewFindingsMerger.Merge([rule], [ai]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void BuildDedupKey_normalises_category_buckets_for_security()
    {
        var ruleKey = ReviewFindingsMerger.BuildDedupKey(RuleFinding(
            "SEC-001",
            "critical",
            "No hardcoded secrets or connection strings",
            "src/Foo.cs",
            1,
            "msg"));

        var aiKey = ReviewFindingsMerger.BuildDedupKey(AiFinding(
            "high",
            "security",
            "src/Foo.cs",
            1,
            "msg"));

        Assert.Equal(ruleKey, aiKey);
    }

    private static FindingWriteDto RuleFinding(
        string ruleId,
        string severity,
        string category,
        string filePath,
        int line,
        string message) =>
        new(
            severity,
            category,
            ruleId,
            FindingSources.Rule,
            filePath,
            line,
            message,
            false,
            "unknown",
            null);

    private static FindingWriteDto AiFinding(
        string severity,
        string category,
        string filePath,
        int line,
        string message) =>
        new(
            severity,
            category,
            null,
            FindingSources.AI,
            filePath,
            line,
            message,
            false,
            "unknown",
            null);
}
