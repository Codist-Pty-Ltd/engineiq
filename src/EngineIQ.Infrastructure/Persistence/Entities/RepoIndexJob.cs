namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class RepoIndexJob
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RepositoryId { get; set; }
    public long InstallationId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string HeadSha { get; set; } = string.Empty;
    public string? BaseSha { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public long? DurationMs { get; set; }
    public int FilesWalked { get; set; }
    public int ChunksTotal { get; set; }
    public int ChunksEmbedded { get; set; }
    public int ChunksDeleted { get; set; }

    public Tenant? Tenant { get; set; }
    public Repository? Repository { get; set; }
}
