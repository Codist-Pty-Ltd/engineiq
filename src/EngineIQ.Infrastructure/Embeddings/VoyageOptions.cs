namespace EngineIQ.Infrastructure.Embeddings;

public class VoyageOptions
{
    public const string SectionName = "Voyage";

    /// <summary>API key from environment / secret injection. Never log.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Voyage embedding model id.</summary>
    public string Model { get; set; } = "voyage-code-3";

    /// <summary>Vector length; must match code_chunks.embedding dimensions.</summary>
    public int Dimensions { get; set; } = 1024;

    /// <summary>Max inputs per embeddings request.</summary>
    public int BatchSize { get; set; } = 96;

    /// <summary>Truncate each input to this many characters before sending.</summary>
    public int MaxInputChars { get; set; } = 24000;

    /// <summary>Backward-compatible alias used by older config keys.</summary>
    public int MaxBatchSize
    {
        get => BatchSize;
        set => BatchSize = value;
    }
}
