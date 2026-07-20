namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class BacklogBackfill
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JiraConnectionId { get; set; }
    public string Jql { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int StartAtCursor { get; set; }
    public int MatchedTotal { get; set; }
    public int EnqueuedCount { get; set; }
    public int SkippedCount { get; set; }
    public int MaxIssues { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    public Tenant? Tenant { get; set; }
    public JiraConnection? JiraConnection { get; set; }
}
