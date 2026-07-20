namespace EngineIQ.API.Jira;

/// <summary>
/// Pure filter rules for Jira Cloud webhook deliveries (testable without hosting the controller).
/// </summary>
public static class JiraWebhookEventFilter
{
    public const string IssueCreatedEvent = "jira:issue_created";
    public const string IssueUpdatedEvent = "jira:issue_updated";

    /// <summary>
    /// Returns true when the delivery should be enqueued for analysis.
    /// For issue_updated, only when the trigger label was newly added (see <see cref="WasTriggerLabelAdded"/>).
    /// </summary>
    public static bool ShouldEnqueue(
        string? webhookEvent,
        string? issueTypeName,
        bool isSubtask,
        string? projectKey,
        string? projectKeysCsv,
        out string skipReason,
        bool labelTriggerAdded = false)
    {
        var isCreated = string.Equals(webhookEvent, IssueCreatedEvent, StringComparison.OrdinalIgnoreCase);
        var isUpdated = string.Equals(webhookEvent, IssueUpdatedEvent, StringComparison.OrdinalIgnoreCase);

        if (!isCreated && !isUpdated)
        {
            skipReason = "ignored_event";
            return false;
        }

        if (isUpdated && !labelTriggerAdded)
        {
            skipReason = "ignored_update";
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

    /// <summary>
    /// True when changelog shows the trigger label added (present in toString, absent from fromString).
    /// Labels in Jira changelog toString/fromString are space-separated.
    /// </summary>
    public static bool WasTriggerLabelAdded(
        IReadOnlyList<JiraChangelogLabelItem>? changelogItems,
        string triggerLabel)
    {
        if (changelogItems is null || changelogItems.Count == 0 || string.IsNullOrWhiteSpace(triggerLabel))
            return false;

        foreach (var item in changelogItems)
        {
            if (!string.Equals(item.Field, "labels", StringComparison.OrdinalIgnoreCase))
                continue;

            var toHas = LabelListContains(item.ToStringValue, triggerLabel);
            var fromHas = LabelListContains(item.FromStringValue, triggerLabel);
            if (toHas && !fromHas)
                return true;
        }

        return false;
    }

    public static bool LabelListContains(string? spaceSeparatedLabels, string triggerLabel)
    {
        if (string.IsNullOrWhiteSpace(spaceSeparatedLabels) || string.IsNullOrWhiteSpace(triggerLabel))
            return false;

        foreach (var part in spaceSeparatedLabels.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, triggerLabel, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    public static string BuildLabelDedupeKey(long issueId, string? updated) =>
        $"{issueId}:label:{updated ?? string.Empty}";
}

public sealed record JiraChangelogLabelItem(string? Field, string? FromStringValue, string? ToStringValue);
