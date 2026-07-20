using Pgvector;

namespace EngineIQ.Infrastructure.Persistence.Entities;

/// <summary>A chunk of code and its embedding for semantic search (Session13 code index). No source is stored elsewhere.</summary>
public sealed class CodeChunk
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RepositoryId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string? SymbolName { get; set; }
    public string? Kind { get; set; }
    public Vector? Embedding { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public Repository? Repository { get; set; }
}
