namespace EngineIQ.Domain.Indexing;

/// <summary>Embedding providers (Voyage) tune vectors differently for indexed content vs. search queries.</summary>
public enum EmbeddingInputType
{
    Document,
    Query
}
