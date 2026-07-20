namespace EngineIQ.ContextBuilder.Search;

public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    public int VectorTopK { get; set; } = 30;
    public int TextTopK { get; set; } = 30;
    public int MaxHits { get; set; } = 16;
    public int MaxHitsPerFile { get; set; } = 3;
    public int MaxContextChars { get; set; } = 30000;

    /// <summary>RRF constant k (standard is 60).</summary>
    public int RrfK { get; set; } = 60;
}
