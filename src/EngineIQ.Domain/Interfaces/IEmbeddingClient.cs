using EngineIQ.Domain.Indexing;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Embedding provider (Voyage) for code chunks and search queries.</summary>
public interface IEmbeddingClient
{
    /// <summary>Vector length produced by this client's configured model.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds a batch of inputs (in memory only). Returned vectors are ordered the same as <paramref name="inputs"/>.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);
}
