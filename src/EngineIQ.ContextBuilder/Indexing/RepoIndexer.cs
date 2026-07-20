using System.Formats.Tar;
using System.IO.Compression;
using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.ContextBuilder.Indexing;

/// <summary>
/// Orchestrates a single repository code-index job: download a tarball snapshot (temp only, deleted
/// before returning), chunk files, embed only chunks whose content hash changed, and upsert/prune
/// <c>code_chunks</c>. Full index when <see cref="RepoIndexJobMessage.BaseSha"/> is null; otherwise
/// incremental via compare (falls back to full when truncated or base is unknown).
/// </summary>
public sealed class RepoIndexer : IRepoIndexer
{
    private readonly IRepoArchiveClient _archive;
    private readonly ICodeChunker _chunker;
    private readonly IEmbeddingClient _embeddings;
    private readonly ICodeChunkRepository _chunks;
    private readonly IRepositoryRepository _repositories;
    private readonly IOptions<IndexingOptions> _options;
    private readonly ILogger<RepoIndexer> _logger;

    public RepoIndexer(
        IRepoArchiveClient archive,
        ICodeChunker chunker,
        IEmbeddingClient embeddings,
        ICodeChunkRepository chunks,
        IRepositoryRepository repositories,
        IOptions<IndexingOptions> options,
        ILogger<RepoIndexer> logger)
    {
        _archive = archive;
        _chunker = chunker;
        _embeddings = embeddings;
        _chunks = chunks;
        _repositories = repositories;
        _options = options;
        _logger = logger;
    }

    public async Task<IndexJobStats> IndexAsync(RepoIndexJobMessage job, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var batchSize = Math.Max(1, opts.EmbedBatchSize);
        // Prompt contract: BaseSha null ⇒ full; set ⇒ incremental (compare BaseSha..HeadSha).
        var fullReindex = string.IsNullOrWhiteSpace(job.BaseSha);

        CompareResult? compare = null;
        if (!fullReindex)
        {
            try
            {
                compare = await _archive.CompareAsync(
                    job.InstallationId, job.Owner, job.Repo, job.BaseSha!, job.HeadSha, cancellationToken);
                if (compare.Truncated)
                {
                    _logger.LogInformation(
                        "Compare truncated for {Owner}/{Repo} {Base}..{Head}; falling back to full index.",
                        job.Owner, job.Repo, job.BaseSha, job.HeadSha);
                    fullReindex = true;
                    compare = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Compare failed for {Owner}/{Repo} base {BaseSha}; falling back to full index.",
                    job.Owner, job.Repo, job.BaseSha);
                fullReindex = true;
                compare = null;
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "engineiq-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var filesWalked = 0;
        var chunksTotal = 0;
        var chunksEmbedded = 0;
        var chunksDeleted = 0;
        var keptPaths = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            await using var tarball = await _archive.DownloadTarballAsync(
                job.InstallationId, job.Owner, job.Repo, job.HeadSha, cancellationToken);
            await ExtractTarballAsync(tarball, tempDir, cancellationToken);
            var rootDir = Directory.GetDirectories(tempDir).FirstOrDefault() ?? tempDir;

            var changedFiles = fullReindex ? WalkAllFiles(rootDir) : compare!.Files;

            var deletePaths = new List<string>();
            foreach (var file in changedFiles)
            {
                if (file.Status == ChangedFileStatus.Removed)
                    deletePaths.Add(file.Path);
                else if (file.Status == ChangedFileStatus.Renamed && !string.IsNullOrWhiteSpace(file.PreviousPath))
                    deletePaths.Add(file.PreviousPath!);
            }

            if (deletePaths.Count > 0)
                chunksDeleted += await _chunks.DeleteByFilePathsAsync(
                    job.TenantId, job.RepositoryId, deletePaths.Distinct().ToList(), cancellationToken);

            var toProcess = changedFiles.Where(f => f.Status != ChangedFileStatus.Removed).ToList();
            var candidatePaths = toProcess.Select(f => f.Path).ToList();
            var existingHashes = candidatePaths.Count > 0
                ? await _chunks.GetHashesForFilesAsync(job.TenantId, job.RepositoryId, candidatePaths, cancellationToken)
                : new Dictionary<string, IReadOnlySet<string>>();

            foreach (var file in toProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsSkippedPath(file.Path, opts.SkipPathSegments))
                    continue;

                var fullPath = Path.Combine(rootDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    continue;

                if (new FileInfo(fullPath).Length > opts.MaxFileSizeKb * 1024L)
                    continue;

                if (LooksBinary(fullPath))
                    continue;

                string content;
                try
                {
                    content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read {Path} for indexing; skipping.", file.Path);
                    continue;
                }

                filesWalked++;
                keptPaths.Add(file.Path);

                var candidates = await _chunker.ChunkAsync(file.Path, content, cancellationToken);
                if (candidates.Count == 0)
                    continue;

                chunksTotal += candidates.Count;
                existingHashes.TryGetValue(file.Path, out var priorHashes);
                priorHashes ??= new HashSet<string>();

                var toEmbed = candidates.Where(c => !priorHashes.Contains(c.ContentSha256)).ToList();
                var rows = new List<CodeChunkEmbeddingRow>(toEmbed.Count);

                foreach (var batch in BatchBy(toEmbed, batchSize))
                {
                    var vectors = await _embeddings.EmbedAsync(
                        batch.Select(c => c.Content).ToList(),
                        EmbeddingInputType.Document,
                        cancellationToken);
                    for (var i = 0; i < batch.Count; i++)
                        rows.Add(new CodeChunkEmbeddingRow(batch[i], vectors[i]));
                }

                chunksEmbedded += toEmbed.Count;

                if (rows.Count > 0)
                    await _chunks.UpsertBatchAsync(job.TenantId, job.RepositoryId, rows, cancellationToken);

                var keepHashes = candidates.Select(c => c.ContentSha256).Distinct().ToList();
                chunksDeleted += await _chunks.DeleteStaleHashesAsync(
                    job.TenantId, job.RepositoryId, file.Path, keepHashes, cancellationToken);
            }

            if (fullReindex)
            {
                chunksDeleted += await _chunks.DeleteExceptFilePathsAsync(
                    job.TenantId, job.RepositoryId, keptPaths.ToList(), cancellationToken);
            }

            await _repositories.SetIndexStateAsync(
                job.TenantId, job.RepositoryId, job.HeadSha, DateTimeOffset.UtcNow, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        _logger.LogInformation(
            "RepoIndexer finished {Owner}/{Repo} Head={Head} Full={Full}: FilesWalked={FilesWalked} ChunksTotal={ChunksTotal} ChunksEmbedded={ChunksEmbedded} ChunksDeleted={ChunksDeleted}",
            job.Owner,
            job.Repo,
            job.HeadSha,
            fullReindex,
            filesWalked,
            chunksTotal,
            chunksEmbedded,
            chunksDeleted);

        return new IndexJobStats(filesWalked, chunksTotal, chunksEmbedded, chunksDeleted);
    }

    private static async Task ExtractTarballAsync(Stream tarball, string destinationDir, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(tarball, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, destinationDir, overwriteFiles: true, cancellationToken);
    }

    private static IReadOnlyList<ChangedFile> WalkAllFiles(string rootDir)
    {
        var files = new List<ChangedFile>();
        foreach (var path in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootDir, path).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(new ChangedFile(relative, ChangedFileStatus.Added));
        }

        return files;
    }

    private static bool IsSkippedPath(string path, IReadOnlyList<string> skipSegments)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => skipSegments.Any(skip => string.Equals(s, skip, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>NUL in the first 8KB ⇒ treat as binary and skip.</summary>
    private static bool LooksBinary(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var buffer = new byte[Math.Min(8192, stream.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<List<T>> BatchBy<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToList();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
