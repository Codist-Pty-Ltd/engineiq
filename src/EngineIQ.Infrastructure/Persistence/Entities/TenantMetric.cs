namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class TenantMetric
{
    public Guid TenantId { get; set; }
    public DateOnly Date { get; set; }
    public int PrsReviewed { get; set; }
    public int ViolationsFound { get; set; }
    public double AvgReviewMs { get; set; }
    public decimal TokenCostZar { get; set; }
    /// <summary>Completed Jira issue-analysis jobs for the UTC day (additive; does not affect PrsReviewed).</summary>
    public int IssuesAnalyzed { get; set; }
    /// <summary>Code chunks embedded by repo-index jobs for the UTC day (additive; Session13).</summary>
    public int ChunksEmbedded { get; set; }

    public Tenant? Tenant { get; set; }
}
