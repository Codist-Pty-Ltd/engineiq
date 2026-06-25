using System.Text;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;

namespace EngineIQ.FeedbackGenerator;

/// <summary>Formats merged findings into a grouped PR review comment (Markdown).</summary>
public static class ReviewCommentFormatter
{
    private static readonly Dictionary<string, string> SeverityHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["critical"] = "Critical",
        ["high"] = "High",
        ["medium"] = "Medium",
        ["warning"] = "Medium",
        ["info"] = "Suggestion",
        ["suggestion"] = "Suggestion",
        ["low"] = "Suggestion",
    };

    public static string Format(
        IReadOnlyList<FindingWriteDto> findings,
        string trustFooter,
        string? aiNarrativeFallback = null)
    {
        if (findings.Count == 0)
        {
            var narrative = string.IsNullOrWhiteSpace(aiNarrativeFallback)
                ? "_No structured findings._"
                : aiNarrativeFallback.Trim();
            return narrative + trustFooter;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## EngineIQ Review");
        sb.AppendLine();

        foreach (var group in GroupByDisplaySeverity(findings))
        {
            sb.AppendLine($"### {group.Heading}");
            foreach (var finding in group.Findings)
            {
                sb.Append("- ");
                sb.Append(FormatLabel(finding));
                if (!string.IsNullOrWhiteSpace(finding.FilePath))
                {
                    sb.Append(' ');
                    sb.Append('`');
                    sb.Append(finding.FilePath);
                    if (finding.LineNumber is int line && line > 0)
                        sb.Append(':').Append(line);
                    sb.Append('`');
                }

                sb.Append(" — ");
                sb.AppendLine(finding.Message.Trim());
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + trustFooter;
    }

    private static IEnumerable<(string Heading, IReadOnlyList<FindingWriteDto> Findings)> GroupByDisplaySeverity(
        IReadOnlyList<FindingWriteDto> findings)
    {
        var grouped = findings
            .GroupBy(f => DisplayHeading(f.Severity))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FindingWriteDto>)g.ToList());

        var order = new[] { "Critical", "High", "Medium", "Suggestion", "Other" };
        foreach (var heading in order)
        {
            if (grouped.TryGetValue(heading, out var list) && list.Count > 0)
                yield return (heading, list);
        }
    }

    private static string DisplayHeading(string severity)
    {
        var key = (severity ?? "medium").Trim().ToLowerInvariant();
        return SeverityHeadings.TryGetValue(key, out var heading) ? heading : "Other";
    }

    private static string FormatLabel(FindingWriteDto finding)
    {
        var source = finding.Source == FindingSources.Rule ? "Rule" : "AI";
        if (!string.IsNullOrWhiteSpace(finding.RuleId))
            return $"**[{finding.RuleId} | {source}]**";

        var category = string.IsNullOrWhiteSpace(finding.Category) ? source : finding.Category;
        return $"**[{category} | {source}]**";
    }
}
