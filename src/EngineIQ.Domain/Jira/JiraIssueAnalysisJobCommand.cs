using EngineIQ.Domain.Messaging;

namespace EngineIQ.Domain.Jira;

public sealed record JiraIssueAnalysisJobCommand(
    Guid TenantId,
    Guid JobId,
    Guid JiraConnectionId,
    string IssueKey,
    long JiraIssueId,
    JiraConnectionInfo Connection,
    AnalysisTrigger Trigger = AnalysisTrigger.Created);
