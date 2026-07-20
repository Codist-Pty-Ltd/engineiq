using System.Text;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;

namespace EngineIQ.AIEngine.IssueImprovement;

public static class IssueImprovementPromptBuilder
{
    public const int MaxDescriptionChars = 8000;
    public const int MaxParentDescriptionChars = 1500;

    /// <summary>Slice 1 system prompt — kept byte-stable when no code context is provided.</summary>
    public static string BuildSystemPrompt(string issueType) => BuildSystemPrompt(issueType, hasCodeContext: false);

    public static string BuildSystemPrompt(string issueType, bool hasCodeContext)
    {
        var type = string.IsNullOrWhiteSpace(issueType) ? "issue" : issueType.Trim();
        if (!hasCodeContext)
        {
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

        return $$"""
            You are EngineIQ analyzing a newly created Jira {{type}}.
            For a Bug: assess whether reproduction steps, expected vs actual behavior, and environment are present. Rewrite the description into a structured bug report. List missing information as direct questions to the reporter. Assess severity (Critical/High/Medium/Low) with one-line justification.
            For a Story: rewrite into user-story form if absent ("As a … I want … so that …"), derive testable acceptance criteria (Given/When/Then), flag ambiguities as questions.
            If the ticket is already well-formed, set isAlreadyWellFormed: true and keep suggestions minimal.

            You are also given retrieved code from the customer's indexed repositories and optional architecture metadata.
            Using ONLY the provided code (never invent file paths not shown), identify likely files to change with a one-line reason and High/Medium/Low confidence; name affected modules/layers per the architecture section; describe blast radius (tests, migrations, callers plausibly affected); give a 3–6 step suggested approach.
            If the provided code appears unrelated to the issue, say so explicitly in blastRadius and set likelyFiles to the closest candidates with Low confidence rather than guessing.
            When no code context was provided, impactAnalysis must be null. When code context is present, always include impactAnalysis.
            Respond ONLY with JSON matching the schema below — no preamble, no markdown fences.

            JSON schema:
            {
              "rewrittenDescription": "string",
              "acceptanceCriteria": ["string"],
              "missingInfoQuestions": ["string"],
              "severityAssessment": "string",
              "isAlreadyWellFormed": true,
              "impactAnalysis": {
                "likelyFiles": [{ "path": "string", "reason": "string", "confidence": "High|Medium|Low" }],
                "affectedModules": ["string"],
                "blastRadius": "string",
                "suggestedApproach": ["string"]
              }
            }
            """.Trim();
    }

    /// <summary>Slice 1 user prompt — byte-stable when code/repo/parent context is absent.</summary>
    public static string BuildUserPrompt(JiraIssueDetails issue) =>
        BuildUserPrompt(issue, codeContext: null, repoContext: null, parent: null);

    public static string BuildUserPrompt(
        JiraIssueDetails issue,
        CodeSearchResult? codeContext,
        RepoContext? repoContext,
        JiraParentSummary? parent = null)
    {
        var hasHits = codeContext is { IsEmpty: false };
        string body;
        if (!hasHits)
            body = BuildSlice1UserPrompt(issue);
        else
        {
            var sb = new StringBuilder();
            sb.Append(BuildSlice1UserPrompt(issue));

            if (repoContext is not null)
            {
                sb.AppendLine();
                sb.AppendLine("## Repository architecture");
                sb.AppendLine($"Detected style: {repoContext.DetectedStyle}");
                if (repoContext.LayerFolderMap.Count > 0)
                {
                    sb.AppendLine("Layer folder map:");
                    foreach (var (layer, folders) in repoContext.LayerFolderMap.OrderBy(kv => kv.Key))
                        sb.AppendLine($"- {layer}: {string.Join(", ", folders)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Relevant code from the indexed codebase");
            foreach (var hit in codeContext!.Hits)
            {
                var symbol = string.IsNullOrWhiteSpace(hit.Symbol) ? "" : $" [{hit.Symbol}]";
                sb.AppendLine($"// {hit.RepositoryName}/{hit.FilePath}{symbol} lines {hit.StartLine}-{hit.EndLine}");
                sb.AppendLine("```");
                sb.AppendLine(hit.Content);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            body = sb.ToString();
        }

        if (parent is null)
            return body;

        return body + BuildParentEpicSection(parent);
    }

    public static string BuildParentEpicSection(JiraParentSummary parent)
    {
        var description = parent.Description ?? string.Empty;
        if (description.Length > MaxParentDescriptionChars)
            description = description[..MaxParentDescriptionChars];

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Parent epic");
        sb.AppendLine($"Key: {parent.Key}");
        sb.AppendLine($"Summary: {parent.Summary}");
        sb.AppendLine("Description:");
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(empty)" : description);
        sb.AppendLine();
        sb.AppendLine(
            "Instruction: acceptance criteria and suggested approach must be consistent with the epic's intent; " +
            "flag contradictions between the story and its epic as a question for the reporter.");
        return sb.ToString();
    }

    private static string BuildSlice1UserPrompt(JiraIssueDetails issue)
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
