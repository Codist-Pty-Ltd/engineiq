using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.FeedbackGenerator;

namespace EngineIQ.Tests.Unit;

public class ReviewPipelineIntegrationTests
{
    [Fact]
    public void Sec001_patch_produces_rule_finding_that_wins_merge_and_formats_in_comment()
    {
        var fixturesDir = LocateFixturesDir();
        var engine = new global::EngineIQ.StandardsEngine.StandardsEngine();
        var patch = File.ReadAllText(Path.Combine(fixturesDir, "sec001-violation.patch"));
        var ruleFindings = engine.EvaluateDiff(patch);

        var secRule = Assert.Single(ruleFindings, f => f.RuleId == "SEC-001");
        Assert.Equal(FindingSources.Rule, secRule.Source);
        Assert.Equal("critical", secRule.Severity);

        var aiDuplicate = new FindingWriteDto(
            "high",
            "security",
            null,
            FindingSources.AI,
            secRule.FilePath,
            secRule.LineNumber,
            "Do not hardcode credentials in source.",
            false,
            "unknown",
            null);

        var aiExtra = new FindingWriteDto(
            "medium",
            "general",
            null,
            FindingSources.AI,
            "README.md",
            1,
            "Consider documenting environment variables.",
            false,
            "unknown",
            null);

        var merged = ReviewFindingsMerger.Merge(ruleFindings, [aiDuplicate, aiExtra]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.RuleId == "SEC-001" && f.Source == FindingSources.Rule);
        Assert.Contains(merged, f => f.Source == FindingSources.AI);

        var comment = ReviewCommentFormatter.Format(merged, "\n\n---\ntrust footer");
        Assert.Contains("### Critical", comment);
        Assert.Contains("**[SEC-001 | Rule]**", comment);
        Assert.Contains("### Medium", comment);
    }

    private static string LocateFixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "python", "rule-validator", "fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate python/rule-validator/fixtures from test output.");
    }
}
