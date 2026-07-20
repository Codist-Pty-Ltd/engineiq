namespace EngineIQ.Domain.Messaging;

/// <summary>Why an issue analysis job was enqueued.</summary>
public enum AnalysisTrigger
{
    Created = 0,
    Label = 1,
    Backfill = 2,
}

/// <summary>
/// Queue payload for Jira issue analysis jobs. No issue content in the message.
/// </summary>
public sealed record JiraIssueAnalysisJobMessage(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId,
    string IssueKey,
    long JiraIssueId,
    int Attempt = 0,
    AnalysisTrigger Trigger = AnalysisTrigger.Created);

/// <summary>Queue payload for paced backlog backfill (pages Jira search and enqueues issue jobs).</summary>
public sealed record BacklogBackfillJobMessage(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId,
    int Attempt = 0);
