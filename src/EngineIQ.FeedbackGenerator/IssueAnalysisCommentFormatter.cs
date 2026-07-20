using System.Text;
using EngineIQ.Domain.Jira;

namespace EngineIQ.FeedbackGenerator;

/// <summary>Formats issue-improvement results into a Jira comment (wiki markup / plain text).</summary>
public static class IssueAnalysisCommentFormatter
{
    public static string Format(IssueImprovementResult result, string trustFooter)
    {
        if (result.IsAlreadyWellFormed)
            return FormatWellFormed(result, trustFooter);

        var sb = new StringBuilder();
        sb.AppendLine("h2. EngineIQ ticket improvement");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.RewrittenDescription))
        {
            sb.AppendLine("h3. Improved description");
            sb.AppendLine(result.RewrittenDescription.Trim());
            sb.AppendLine();
        }

        AppendBulleted(sb, "Acceptance criteria", result.AcceptanceCriteria);
        AppendImpactAnalysis(sb, result.ImpactAnalysis);
        AppendBulleted(sb, "Questions for reporter", result.MissingInfoQuestions);

        if (!string.IsNullOrWhiteSpace(result.SeverityAssessment))
        {
            sb.AppendLine("h3. Severity");
            sb.AppendLine(result.SeverityAssessment.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + trustFooter;
    }

    private static string FormatWellFormed(IssueImprovementResult result, string trustFooter)
    {
        var sb = new StringBuilder();
        sb.AppendLine("h2. EngineIQ review");
        sb.AppendLine();
        sb.AppendLine("This ticket looks well-formed. Only minor additions below (if any).");
        sb.AppendLine();
        AppendBulleted(sb, "Suggested acceptance criteria", result.AcceptanceCriteria);
        AppendImpactAnalysis(sb, result.ImpactAnalysis);
        AppendBulleted(sb, "Optional clarifications", result.MissingInfoQuestions);
        if (!string.IsNullOrWhiteSpace(result.SeverityAssessment))
        {
            sb.AppendLine("h3. Severity note");
            sb.AppendLine(result.SeverityAssessment.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + trustFooter;
    }

    internal static void AppendImpactAnalysis(StringBuilder sb, IssueImpactAnalysis? impact)
    {
        if (impact is null)
            return;

        sb.AppendLine("h3. Impact Analysis");

        if (impact.LikelyFiles.Count > 0)
        {
            sb.AppendLine("*Likely files:*");
            foreach (var file in impact.LikelyFiles)
            {
                sb.Append("- ");
                sb.Append(file.Path);
                sb.Append(" — ");
                sb.Append(file.Reason);
                sb.Append(" _(");
                sb.Append(file.Confidence);
                sb.AppendLine(" confidence)_");
            }
        }

        if (impact.AffectedModules.Count > 0)
        {
            sb.Append("*Affected modules:* ");
            sb.AppendLine(string.Join(", ", impact.AffectedModules));
        }

        if (!string.IsNullOrWhiteSpace(impact.BlastRadius))
        {
            sb.Append("*Blast radius:* ");
            sb.AppendLine(impact.BlastRadius.Trim());
        }

        if (impact.SuggestedApproach.Count > 0)
        {
            sb.AppendLine("*Suggested approach:*");
            foreach (var step in impact.SuggestedApproach)
            {
                if (string.IsNullOrWhiteSpace(step))
                    continue;
                sb.Append("# ");
                sb.AppendLine(step.Trim());
            }
        }

        sb.AppendLine();
    }

    private static void AppendBulleted(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine($"h3. {heading}");
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;
            sb.Append("* ");
            sb.AppendLine(item.Trim());
        }

        sb.AppendLine();
    }
}
