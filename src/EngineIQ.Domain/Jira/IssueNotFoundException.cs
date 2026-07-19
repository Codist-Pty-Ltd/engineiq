namespace EngineIQ.Domain.Jira;

/// <summary>Non-retryable: Jira issue no longer exists or is inaccessible.</summary>
public sealed class IssueNotFoundException : Exception
{
    public IssueNotFoundException(string issueKey)
        : base($"issue_not_found:{issueKey}")
    {
        IssueKey = issueKey;
    }

    public string IssueKey { get; }
}
