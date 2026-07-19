namespace EngineIQ.Domain.Messaging;

/// <summary>
/// Queue payload for Jira issue analysis jobs. No issue content in the message.
/// </summary>
public sealed record JiraIssueAnalysisJobMessage(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId,
    string IssueKey,
    long JiraIssueId,
    int Attempt = 0);
