namespace EngineIQ.API.Jira;

/// <summary>
/// Pure filter rules for Jira Cloud webhook deliveries (testable without hosting the controller).
/// </summary>
public static class JiraWebhookEventFilter
{
    public const string IssueCreatedEvent = "jira:issue_created";

    /// <summary>
    /// Returns true when the delivery should be enqueued for analysis.
    /// </summary>
    public static bool ShouldEnqueue(
        string? webhookEvent,
        string? issueTypeName,
        bool isSubtask,
        string? projectKey,
        string? projectKeysCsv,
        out string skipReason)
    {
        if (!string.Equals(webhookEvent, IssueCreatedEvent, StringComparison.OrdinalIgnoreCase))
        {
            skipReason = "ignored_event";
            return false;
        }

        if (isSubtask)
        {
            skipReason = "subtask";
            return false;
        }

        if (!IsBugOrStory(issueTypeName))
        {
            skipReason = "ignored_issue_type";
            return false;
        }

        if (!IsProjectAllowed(projectKey, projectKeysCsv))
        {
            skipReason = "project_not_listed";
            return false;
        }

        skipReason = string.Empty;
        return true;
    }

    public static bool IsBugOrStory(string? issueTypeName) =>
        string.Equals(issueTypeName, "Bug", StringComparison.OrdinalIgnoreCase)
        || string.Equals(issueTypeName, "Story", StringComparison.OrdinalIgnoreCase);

    public static bool IsProjectAllowed(string? projectKey, string? projectKeysCsv)
    {
        if (string.IsNullOrWhiteSpace(projectKeysCsv))
            return true;

        if (string.IsNullOrWhiteSpace(projectKey))
            return false;

        foreach (var part in projectKeysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, projectKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string BuildDedupeKey(long issueId, string? updated) =>
        $"{issueId}:{updated ?? string.Empty}";
}
