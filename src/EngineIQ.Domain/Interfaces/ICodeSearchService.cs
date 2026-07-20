using EngineIQ.Domain.Search;

namespace EngineIQ.Domain.Interfaces;

public interface ICodeSearchService
{
    /// <summary>
    /// Hybrid vector + full-text search over indexed repos. Never throws for empty inputs;
    /// returns <see cref="CodeSearchResult.Empty"/> when nothing matches.
    /// </summary>
    Task<CodeSearchResult> SearchAsync(
        Guid tenantId,
        IReadOnlyList<Guid> repositoryIds,
        string queryText,
        CancellationToken cancellationToken = default);
}
