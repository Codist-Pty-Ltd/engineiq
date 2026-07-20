using EngineIQ.Domain.Messaging;

namespace EngineIQ.Domain.Interfaces;

/// <summary>Orchestrates a single repository code-index job: download, chunk, embed, upsert.</summary>
public interface IRepoIndexer
{
    Task<IndexJobStats> IndexAsync(RepoIndexJobMessage job, CancellationToken cancellationToken = default);
}

public sealed record IndexJobStats(int FilesWalked, int ChunksTotal, int ChunksEmbedded, int ChunksDeleted);
