using System.Text.RegularExpressions;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.StandardsEngine.Config;
using EngineIQ.StandardsEngine.Parsing;

namespace EngineIQ.StandardsEngine.Rules;

internal sealed record RuleViolation(
    string RuleId,
    string Severity,
    string Category,
    string FilePath,
    int LineNumber,
    string Message);

internal static class DeterministicRuleExecutor
{
    private static readonly string[] Arch001DisallowedTokens =
        ["Infrastructure", "API", "Persistence", "Controllers", "WebAPI"];

    private static readonly string[] Arch001NamespaceTokens =
        ["Infrastructure.", ".API.", "Persistence."];

    private static readonly Regex IfPattern = new(@"\bif\s*\(", RegexOptions.Compiled);
    private static readonly Regex SwitchPattern = new(@"\bswitch\s*\(", RegexOptions.Compiled);
    private static readonly Regex CalculationPattern = new(
        @"(?<![\w.])(?:var|let|const)?\s*\w+\s*=\s*[^;]*[+\-*/%][^;]*;|" +
        @"\b(?:total|sum|amount|price|cost)\s*=\s*[^;]+[+\-*/%][^;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] SecretPatterns =
    [
        new(@"(?i)connection\s*string\s*=\s*[""'][^""']+[""']", RegexOptions.Compiled),
        new(@"(?i)(password|api[_-]?key|secret|token)\s*=\s*[""'][^""']{8,}[""']", RegexOptions.Compiled),
        new(@"(?i)Password=[^""';\s]{8,}", RegexOptions.Compiled),
        new(@"(?i)Host=[^""';]+;Password=[^""';]+", RegexOptions.Compiled),
        new(@"sk-ant-[A-Za-z0-9_-]{10,}", RegexOptions.Compiled),
        new(@"-----BEGIN (RSA |EC )?PRIVATE KEY-----", RegexOptions.Compiled),
    ];

    private static readonly Regex[] PerfPatterns =
    [
        new(@"\.Result\b", RegexOptions.Compiled),
        new(@"\.Wait\(\)", RegexOptions.Compiled),
        new(@"GetAwaiter\(\)\.GetResult\(\)", RegexOptions.Compiled),
    ];

    public static IReadOnlyList<FindingWriteDto> Execute(
        IReadOnlyList<DiffHunk> hunks,
        StandardsConfigDocument config,
        RepoContext? repoContext = null)
    {
        var rulesById = config.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var violations = new List<RuleViolation>();
        if (rulesById.ContainsKey("ARCH-001"))
            violations.AddRange(CheckArch001(hunks, rulesById["ARCH-001"], repoContext));
        if (rulesById.ContainsKey("ARCH-002"))
            violations.AddRange(CheckArch002(hunks, rulesById["ARCH-002"], repoContext));
        if (rulesById.ContainsKey("SEC-001"))
            violations.AddRange(CheckSec001(hunks, rulesById["SEC-001"]));
        if (rulesById.ContainsKey("PERF-001"))
            violations.AddRange(CheckPerf001(hunks, rulesById["PERF-001"]));

        return violations
            .DistinctBy(v => (v.RuleId, v.FilePath, v.LineNumber, v.Message))
            .Select(ToFinding)
            .ToList();
    }

    private static IEnumerable<RuleViolation> CheckArch001(
        IReadOnlyList<DiffHunk> hunks,
        StandardsRuleDefinition rule,
        RepoContext? repoContext)
    {
        foreach (var hunk in hunks)
        {
            if (!LayerPathMatcher.IsInLayer(hunk.Path, "Domain", repoContext))
                continue;

            foreach (var added in hunk.AddedLines)
            {
                if (added.Text.Contains("using ", StringComparison.Ordinal))
                {
                    foreach (var token in Arch001DisallowedTokens)
                    {
                        if (added.Text.Contains(token, StringComparison.Ordinal))
                        {
                            yield return Violation(
                                rule,
                                hunk.Path,
                                added.LineNumber,
                                $"Domain layer must not reference {token}. Move this dependency behind an Application abstraction and implement it in Infrastructure.");
                            break;
                        }
                    }
                }

                foreach (var token in Arch001NamespaceTokens)
                {
                    if (added.Text.Contains(token, StringComparison.Ordinal))
                    {
                        yield return Violation(
                            rule,
                            hunk.Path,
                            added.LineNumber,
                            $"Domain layer must not reference {token}. Keep Domain free of outer-layer types.");
                    }
                }
            }
        }
    }

    private static IEnumerable<RuleViolation> CheckArch002(
        IReadOnlyList<DiffHunk> hunks,
        StandardsRuleDefinition rule,
        RepoContext? repoContext)
    {
        foreach (var hunk in hunks)
        {
            if (!LayerPathMatcher.IsInLayer(hunk.Path, "API", repoContext)
                && !LayerPathMatcher.IsInLayer(hunk.Path, "Presentation", repoContext))
                continue;

            foreach (var added in hunk.AddedLines)
            {
                var stripped = added.Text.Trim();
                if (stripped.StartsWith("//", StringComparison.Ordinal) || stripped.StartsWith('*'))
                    continue;

                if (IfPattern.IsMatch(added.Text))
                {
                    yield return Violation(
                        rule,
                        hunk.Path,
                        added.LineNumber,
                        "Business logic (if) in controller. Move branching into an application service or use case.");
                }
                else if (SwitchPattern.IsMatch(added.Text))
                {
                    yield return Violation(
                        rule,
                        hunk.Path,
                        added.LineNumber,
                        "Business logic (switch) in controller. Replace with polymorphism or an application service.");
                }
                else if (CalculationPattern.IsMatch(added.Text))
                {
                    yield return Violation(
                        rule,
                        hunk.Path,
                        added.LineNumber,
                        "Calculation logic in controller. Perform calculations in Domain or Application, not in the API layer.");
                }
            }
        }
    }

    private static IEnumerable<RuleViolation> CheckSec001(IReadOnlyList<DiffHunk> hunks, StandardsRuleDefinition rule)
    {
        foreach (var hunk in hunks)
        {
            foreach (var added in hunk.AddedLines)
            {
                if (SecretPatterns.Any(p => p.IsMatch(added.Text)))
                {
                    yield return Violation(
                        rule,
                        hunk.Path,
                        added.LineNumber,
                        "Possible hardcoded secret. Load credentials from IConfiguration, environment variables, or a secret manager — never commit literals.");
                }
            }
        }
    }

    private static IEnumerable<RuleViolation> CheckPerf001(IReadOnlyList<DiffHunk> hunks, StandardsRuleDefinition rule)
    {
        foreach (var hunk in hunks)
        {
            foreach (var added in hunk.AddedLines)
            {
                if (PerfPatterns.Any(p => p.IsMatch(added.Text)))
                {
                    yield return Violation(
                        rule,
                        hunk.Path,
                        added.LineNumber,
                        "Blocking async call pattern detected. Await the Task end-to-end instead of using .Result, .Wait(), or GetAwaiter().GetResult().");
                }
            }
        }
    }

    private static RuleViolation Violation(
        StandardsRuleDefinition rule,
        string filePath,
        int lineNumber,
        string message) =>
        new(
            rule.Id,
            NormalizeSeverity(rule.Severity),
            string.IsNullOrWhiteSpace(rule.Name) ? rule.Check : rule.Name,
            filePath,
            lineNumber,
            message);

    private static string NormalizeSeverity(string severity) =>
        (severity ?? "high").Trim().ToLowerInvariant() switch
        {
            "warn" or "warning" => "high",
            "info" => "medium",
            _ => (severity ?? "high").Trim().ToLowerInvariant(),
        };

    private static FindingWriteDto ToFinding(RuleViolation violation) =>
        new(
            Severity: violation.Severity,
            Category: violation.Category,
            RuleId: violation.RuleId,
            Source: FindingSources.Rule,
            FilePath: violation.FilePath,
            LineNumber: violation.LineNumber,
            Message: violation.Message,
            WasActioned: false,
            PrMergeStatus: "unknown",
            TrainingFeaturesJson: null);
}
