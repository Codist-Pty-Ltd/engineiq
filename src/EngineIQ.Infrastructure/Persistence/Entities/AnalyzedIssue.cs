namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class AnalyzedIssue
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JiraConnectionId { get; set; }
    public long JiraIssueId { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public string JiraCommentId { get; set; } = string.Empty;
    public DateTimeOffset LastAnalyzedIssueUpdatedAt { get; set; }
    public string LastTrigger { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public JiraConnection? JiraConnection { get; set; }
}
