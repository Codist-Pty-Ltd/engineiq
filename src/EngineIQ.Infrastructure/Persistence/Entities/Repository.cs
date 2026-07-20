namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class Repository
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ArchitectureStyle { get; set; }
    public DateTimeOffset? IndexedAt { get; set; }
    /// <summary>Head commit sha of the last successful code-index job (Session13); null before the first index.</summary>
    public string? IndexedCommitSha { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<PrReviewJob> Jobs { get; set; } = new List<PrReviewJob>();
}
