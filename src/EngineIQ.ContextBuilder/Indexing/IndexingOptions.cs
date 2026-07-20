namespace EngineIQ.ContextBuilder.Indexing;

public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>Files larger than this are skipped (likely generated/binary, not worth chunking).</summary>
    public int MaxFileSizeKb { get; set; } = 200;

    /// <summary>Embedding batch size (must match Voyage:BatchSize default).</summary>
    public int EmbedBatchSize { get; set; } = 96;

    public List<string> SkipPathSegments { get; set; } = new()
    {
        "node_modules", "bin", "obj", "dist", ".git", "packages", "vendor", ".angular",
        "build", ".venv", "__pycache__", ".next"
    };

    /// <summary>Hard timeout for a single index job (Worker consumer cancellation budget).</summary>
    public int JobTimeoutMinutes { get; set; } = 10;
}
