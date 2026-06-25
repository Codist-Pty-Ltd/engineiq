using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIQ.Domain.Persistence;

namespace EngineIQ.Tests.Unit;

public class StandardsEngineRuleTests
{
    private static readonly string FixturesDir = LocateFixturesDir();

    private readonly global::EngineIQ.StandardsEngine.StandardsEngine _engine = new();

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

    [Fact]
    public void EvaluateDiff_with_null_yaml_uses_embedded_clean_architecture_default()
    {
        var patch = File.ReadAllText(Path.Combine(FixturesDir, "arch001-violation.patch"));
        var findings = _engine.EvaluateDiff(patch);

        Assert.Contains(findings, f => f.RuleId == "ARCH-001" && f.Source == FindingSources.Rule);
    }

    [Theory]
    [InlineData("arch001-violation.patch", "ARCH-001", true)]
    [InlineData("arch001-clean.patch", "ARCH-001", false)]
    [InlineData("arch002-violation.patch", "ARCH-002", true)]
    [InlineData("arch002-clean.patch", "ARCH-002", false)]
    [InlineData("sec001-violation.patch", "SEC-001", true)]
    [InlineData("sec001-clean.patch", "SEC-001", false)]
    [InlineData("perf001-violation.patch", "PERF-001", true)]
    [InlineData("perf001-clean.patch", "PERF-001", false)]
    public void EvaluateDiff_matches_rule_validator_fixtures(string fixture, string ruleId, bool shouldFind)
    {
        var patch = File.ReadAllText(Path.Combine(FixturesDir, fixture));
        var findings = _engine.EvaluateDiff(patch);

        var fired = findings.Any(f => string.Equals(f.RuleId, ruleId, StringComparison.Ordinal));
        Assert.Equal(shouldFind, fired);
    }

    [Fact]
    public void EvaluateDiff_rule_findings_include_required_metadata()
    {
        var patch = File.ReadAllText(Path.Combine(FixturesDir, "sec001-violation.patch"));
        var finding = Assert.Single(_engine.EvaluateDiff(patch), f => f.RuleId == "SEC-001");

        Assert.Equal(FindingSources.Rule, finding.Source);
        Assert.Equal("critical", finding.Severity);
        Assert.False(string.IsNullOrWhiteSpace(finding.Category));
        Assert.False(string.IsNullOrWhiteSpace(finding.FilePath));
        Assert.True(finding.LineNumber > 0);
        Assert.Contains("secret", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IConfiguration", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateDiff_overall_false_positive_rate_below_validator_threshold()
    {
        var labels = JsonSerializer.Deserialize<Dictionary<string, FixtureLabel>>(
            File.ReadAllText(Path.Combine(FixturesDir, "labels.json")))!;

        var falsePositives = 0;
        var trueNegatives = 0;

        foreach (var (fixture, label) in labels)
        {
            if (label.ShouldFind)
                continue;

            var patch = File.ReadAllText(Path.Combine(FixturesDir, fixture));
            var fired = _engine.EvaluateDiff(patch)
                .Any(f => string.Equals(f.RuleId, label.RuleId, StringComparison.Ordinal));

            if (fired)
                falsePositives++;
            else
                trueNegatives++;
        }

        var overallFpRate = trueNegatives + falsePositives == 0
            ? 0
            : (double)falsePositives / (falsePositives + trueNegatives);

        Assert.True(overallFpRate < 0.15, $"Overall FP rate {overallFpRate:P1} must stay below 15%.");
    }

    private sealed record FixtureLabel(
        [property: JsonPropertyName("rule_id")] string RuleId,
        [property: JsonPropertyName("should_find")] bool ShouldFind);
}
