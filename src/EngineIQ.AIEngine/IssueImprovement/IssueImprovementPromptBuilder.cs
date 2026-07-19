using System.Text;
using EngineIQ.Domain.Jira;

namespace EngineIQ.AIEngine.IssueImprovement;

public static class IssueImprovementPromptBuilder
{
    public const int MaxDescriptionChars = 8000;

    public static string BuildSystemPrompt(string issueType)
    {
        var type = string.IsNullOrWhiteSpace(issueType) ? "issue" : issueType.Trim();
        return $$"""
            You are EngineIQ analyzing a newly created Jira {{type}}.
            For a Bug: assess whether reproduction steps, expected vs actual behavior, and environment are present. Rewrite the description into a structured bug report. List missing information as direct questions to the reporter. Assess severity (Critical/High/Medium/Low) with one-line justification.
            For a Story: rewrite into user-story form if absent ("As a … I want … so that …"), derive testable acceptance criteria (Given/When/Then), flag ambiguities as questions.
            If the ticket is already well-formed, set isAlreadyWellFormed: true and keep suggestions minimal.
            Respond ONLY with JSON matching the schema below — no preamble, no markdown fences.

            JSON schema:
            {
              "rewrittenDescription": "string",
              "acceptanceCriteria": ["string"],
              "missingInfoQuestions": ["string"],
              "severityAssessment": "string",
              "isAlreadyWellFormed": true
            }
            """.Trim();
    }

    public static string BuildUserPrompt(JiraIssueDetails issue)
    {
        var description = issue.Description ?? string.Empty;
        var truncated = false;
        if (description.Length > MaxDescriptionChars)
        {
            description = description[..MaxDescriptionChars];
            truncated = true;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Issue key: {issue.IssueKey}");
        sb.AppendLine($"Type: {issue.IssueType}");
        sb.AppendLine($"Project: {issue.ProjectKey}");
        sb.AppendLine($"Priority: {issue.Priority ?? "(none)"}");
        sb.AppendLine($"Reporter: {issue.Reporter ?? "(unknown)"}");
        sb.AppendLine($"Summary: {issue.Summary}");
        sb.AppendLine();
        sb.AppendLine("Description:");
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(empty)" : description);
        if (truncated)
        {
            sb.AppendLine();
            sb.AppendLine($"[Note: description truncated to {MaxDescriptionChars} characters for analysis.]");
        }

        return sb.ToString();
    }
}
