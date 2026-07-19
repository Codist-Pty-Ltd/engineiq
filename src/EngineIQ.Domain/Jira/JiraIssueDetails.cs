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
    DateTimeOffset? UpdatedAt);
