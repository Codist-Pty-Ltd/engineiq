using EngineIQ.Domain.Messaging;

namespace EngineIQ.Domain.Interfaces;

public interface IAnalyzedIssueRepository
{
    Task<AnalyzedIssueRow?> GetByIssueAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        long jiraIssueId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        long jiraIssueId,
        string issueKey,
        string jiraCommentId,
        DateTimeOffset lastAnalyzedIssueUpdatedAt,
        AnalysisTrigger trigger,
        CancellationToken cancellationToken = default);
}

public sealed record AnalyzedIssueRow(
    Guid Id,
    Guid JiraConnectionId,
    long JiraIssueId,
    string IssueKey,
    string JiraCommentId,
    DateTimeOffset LastAnalyzedIssueUpdatedAt,
    AnalysisTrigger LastTrigger);
