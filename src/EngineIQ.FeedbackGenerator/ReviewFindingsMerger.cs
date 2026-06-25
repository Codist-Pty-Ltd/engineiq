using System.Text;
using System.Text.RegularExpressions;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;

namespace EngineIQ.FeedbackGenerator;

/// <summary>Merges rule and AI findings; deterministic rule rows win on duplicate keys.</summary>
public static class ReviewFindingsMerger
{
    public static string BuildDedupKey(FindingWriteDto finding)
    {
        var path = NormalizePath(finding.FilePath);
        var line = finding.LineNumber ?? 0;
        var category = NormalizeCategory(finding.Category);
        return $"{path}|{line}|{category}";
    }

    public static IReadOnlyList<FindingWriteDto> Merge(
        IReadOnlyList<FindingWriteDto> ruleFindings,
        IReadOnlyList<FindingWriteDto> aiFindings)
    {
        var merged = new List<FindingWriteDto>(ruleFindings);
        var ruleKeys = new HashSet<string>(ruleFindings.Select(BuildDedupKey), StringComparer.Ordinal);

        foreach (var ai in aiFindings)
        {
            if (ruleKeys.Contains(BuildDedupKey(ai)))
                continue;
            merged.Add(ai);
        }

        return merged;
    }

    internal static string NormalizePath(string? filePath) =>
        (filePath ?? string.Empty).Replace('\\', '/').Trim().ToLowerInvariant();

    internal static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return string.Empty;

        var c = category.Trim().ToLowerInvariant();
        if (c.Contains("secret", StringComparison.Ordinal)
            || c.Contains("security", StringComparison.Ordinal)
            || c.Contains("hardcoded", StringComparison.Ordinal))
        {
            return "security";
        }

        if (c.Contains("architecture", StringComparison.Ordinal)
            || c.Contains("layer", StringComparison.Ordinal)
            || c.Contains("domain", StringComparison.Ordinal)
            || c.Contains("controller", StringComparison.Ordinal))
        {
            return "architecture";
        }

        if (c.Contains("async", StringComparison.Ordinal)
            || c.Contains("performance", StringComparison.Ordinal)
            || c.Contains("blocking", StringComparison.Ordinal))
        {
            return "performance";
        }

        return Regex.Replace(c, @"\s+", " ").Trim();
    }
}
