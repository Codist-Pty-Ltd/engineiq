using EngineIQ.Domain.Jira;

namespace EngineIQ.Domain.Interfaces;

public interface IJiraClient
{
    Task<JiraIssueDetails?> GetIssueAsync(
        JiraConnectionInfo connection,
        string issueKey,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a comment; returns the created Jira comment id.</summary>
    Task<string> PostCommentAsync(
        JiraConnectionInfo connection,
        string issueKey,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing comment. Returns the comment id on success; null when the comment is gone (404).
    /// </summary>
    Task<string?> UpdateCommentAsync(
        JiraConnectionInfo connection,
        string issueKey,
        string commentId,
        string body,
        CancellationToken cancellationToken = default);

    Task<JiraSearchPage> SearchIssuesAsync(
        JiraConnectionInfo connection,
        string jql,
        int startAt,
        int maxResults,
        CancellationToken cancellationToken = default);

    Task<JiraParentSummary?> GetParentAsync(
        JiraConnectionInfo connection,
        string parentKey,
        CancellationToken cancellationToken = default);
}
