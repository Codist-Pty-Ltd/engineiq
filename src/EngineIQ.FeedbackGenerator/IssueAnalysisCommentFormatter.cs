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
        AppendBulleted(sb, "Optional clarifications", result.MissingInfoQuestions);
        if (!string.IsNullOrWhiteSpace(result.SeverityAssessment))
        {
            sb.AppendLine("h3. Severity note");
            sb.AppendLine(result.SeverityAssessment.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + trustFooter;
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
