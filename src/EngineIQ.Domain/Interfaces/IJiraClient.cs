using EngineIQ.Domain.Jira;

namespace EngineIQ.Domain.Interfaces;

public interface IJiraClient
{
    Task<JiraIssueDetails?> GetIssueAsync(
        JiraConnectionInfo connection,
        string issueKey,
        CancellationToken cancellationToken = default);

    Task PostCommentAsync(
        JiraConnectionInfo connection,
        string issueKey,
        string body,
        CancellationToken cancellationToken = default);
}
