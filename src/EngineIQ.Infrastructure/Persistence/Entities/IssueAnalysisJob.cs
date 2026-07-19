namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class IssueAnalysisJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JiraConnectionId { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public long JiraIssueId { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public long? DurationMs { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? EstimatedCostZar { get; set; }

    public Tenant? Tenant { get; set; }
    public JiraConnection? JiraConnection { get; set; }
}
