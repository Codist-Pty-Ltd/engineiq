namespace EngineIQ.Domain.Jira;

public sealed record JiraIssueDetails(
    string IssueKey,
    long JiraIssueId,
    string IssueType,
    string Summary,
    string? Description,
    string? Priority,
    string? Reporter,
    string ProjectKey,
    DateTimeOffset? UpdatedAt,
    string? ParentKey = null);

public sealed record JiraSearchPage(int Total, int StartAt, IReadOnlyList<JiraSearchIssue> Issues);

public sealed record JiraSearchIssue(long Id, string Key, string IssueType, DateTimeOffset UpdatedAt);

public sealed record JiraParentSummary(string Key, string Summary, string? Description);

/// <summary>Thrown when Jira rejects a JQL search (HTTP 400). Non-retryable for backfill.</summary>
public sealed class InvalidJqlException : Exception
{
    public InvalidJqlException(string message) : base(message)
    {
    }
}
