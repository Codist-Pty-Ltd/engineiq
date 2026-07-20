using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using EngineIQ.ContextBuilder.Indexing;
using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class RepoIndexerTests
{
    [Fact]
    public async Task IndexAsync_full_reindex_embeds_every_chunk_and_sets_index_state()
    {
        var files = new Dictionary<string, string>
        {
            ["src/Calculator.cs"] = """
                namespace Sample;

                public class Calculator
                {
                    public int Add(int a, int b)
                    {
                        return a + b;
                    }
                }
                """,
            ["README.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"readme line {i}")),
        };

        var archive = new FakeRepoArchiveClient(files);
        var chunkRepo = new FakeCodeChunkRepository();
        var repositories = new FakeRepositoryRepository(indexedCommitSha: null);
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", null);

        var stats = await indexer.IndexAsync(job);

        Assert.Equal(2, stats.FilesWalked);
        Assert.True(stats.ChunksTotal > 0);
        Assert.Equal(stats.ChunksTotal, stats.ChunksEmbedded);
        Assert.Equal(stats.ChunksTotal, embeddings.EmbeddedInputs.Count);
        Assert.True(chunkRepo.UpsertedRows.Count > 0);
        Assert.Equal(job.HeadSha, repositories.LastSetCommitSha);
        Assert.Equal(job.TenantId, repositories.LastSetTenantId);
    }

    [Fact]
    public async Task IndexAsync_skips_embedding_for_unchanged_chunk_hash()
    {
        const string content = "unchanged line 1\nunchanged line 2\nunchanged line 3\nunchanged line 4\nunchanged line 5";
        var files = new Dictionary<string, string> { ["docs/notes.md"] = content };

        var chunker = new CompositeCodeChunker();
        var expected = await chunker.ChunkAsync("docs/notes.md", content);

        var archive = new FakeRepoArchiveClient(files)
        {
            CompareFiles = new[] { new ChangedFile("docs/notes.md", ChangedFileStatus.Modified) },
        };
        var chunkRepo = new FakeCodeChunkRepository();
        foreach (var c in expected)
            chunkRepo.SeedHash(c.FilePath, c.ContentSha256);

        var repositories = new FakeRepositoryRepository(indexedCommitSha: "sha-base");
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", "sha-base");

        var stats = await indexer.IndexAsync(job);

        Assert.Equal(1, stats.FilesWalked);
        Assert.Equal(0, stats.ChunksEmbedded);
        Assert.Empty(embeddings.EmbeddedInputs);
    }

    [Fact]
    public async Task IndexAsync_base_sha_null_forces_full_even_when_repo_already_indexed()
    {
        var files = new Dictionary<string, string>
        {
            ["README.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}")),
        };

        var archive = new FakeRepoArchiveClient(files)
        {
            CompareShouldThrow = true,
        };
        var chunkRepo = new FakeCodeChunkRepository();
        var repositories = new FakeRepositoryRepository(indexedCommitSha: "old-sha");
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", BaseSha: null);

        var stats = await indexer.IndexAsync(job);

        Assert.Equal(0, archive.CompareCalls);
        Assert.True(stats.ChunksEmbedded > 0);
    }

    [Fact]
    public async Task IndexAsync_truncated_compare_falls_back_to_full_index()
    {
        var files = new Dictionary<string, string>
        {
            ["a.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"a {i}")),
            ["b.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"b {i}")),
        };

        var archive = new FakeRepoArchiveClient(files)
        {
            CompareFiles = new[] { new ChangedFile("a.md", ChangedFileStatus.Modified) },
            CompareTruncated = true,
        };
        var chunkRepo = new FakeCodeChunkRepository();
        var repositories = new FakeRepositoryRepository(indexedCommitSha: "sha-base");
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", "sha-base");

        var stats = await indexer.IndexAsync(job);

        Assert.Equal(2, stats.FilesWalked);
        Assert.True(stats.ChunksEmbedded > 0);
    }

    [Fact]
    public async Task IndexAsync_renamed_file_deletes_previous_path()
    {
        var files = new Dictionary<string, string>
        {
            ["new/path.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}")),
        };

        var archive = new FakeRepoArchiveClient(files)
        {
            CompareFiles = new[]
            {
                new ChangedFile("new/path.md", ChangedFileStatus.Renamed, PreviousPath: "old/path.md"),
            },
        };
        var chunkRepo = new FakeCodeChunkRepository();
        chunkRepo.SeedHash("old/path.md", "deadbeef");
        var repositories = new FakeRepositoryRepository(indexedCommitSha: "sha-base");
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", "sha-base");

        var stats = await indexer.IndexAsync(job);

        Assert.DoesNotContain("old/path.md", chunkRepo.HashesByFile.Keys);
        Assert.True(stats.ChunksDeleted >= 1);
        Assert.True(stats.ChunksEmbedded > 0);
    }

    [Fact]
    public async Task IndexAsync_removed_file_deletes_chunks()
    {
        // Tarball still required by the pipeline; keep an unrelated file so the archive is valid.
        var files = new Dictionary<string, string>
        {
            ["keep.md"] = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"keep {i}")),
        };
        var archive = new FakeRepoArchiveClient(files)
        {
            CompareFiles = new[] { new ChangedFile("gone.md", ChangedFileStatus.Removed) },
        };
        var chunkRepo = new FakeCodeChunkRepository();
        chunkRepo.SeedHash("gone.md", "abc");
        var repositories = new FakeRepositoryRepository(indexedCommitSha: "sha-base");
        var embeddings = new FakeEmbeddingClient();
        var indexer = BuildIndexer(archive, chunkRepo, repositories, embeddings);

        var job = new RepoIndexJobMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, "codist", "engineiq", "sha-head", "sha-base");

        var stats = await indexer.IndexAsync(job);

        Assert.DoesNotContain("gone.md", chunkRepo.HashesByFile.Keys);
        Assert.Equal(1, stats.ChunksDeleted);
        Assert.Equal(0, stats.ChunksEmbedded);
    }

    private static RepoIndexer BuildIndexer(
        IRepoArchiveClient archive,
        ICodeChunkRepository chunkRepo,
        IRepositoryRepository repositories,
        IEmbeddingClient embeddings) =>
        new(
            archive,
            new CompositeCodeChunker(),
            embeddings,
            chunkRepo,
            repositories,
            Options.Create(new IndexingOptions()),
            NullLogger<RepoIndexer>.Instance);

    private static Stream BuildTarballGz(IReadOnlyDictionary<string, string> files)
    {
        var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, $"repo-root/{path}")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                };
                writer.WriteEntry(entry);
            }
        }

        memory.Position = 0;
        return memory;
    }

    private sealed class FakeRepoArchiveClient : IRepoArchiveClient
    {
        private readonly Dictionary<string, string> _files;

        public FakeRepoArchiveClient(Dictionary<string, string> files) => _files = files;

        public IReadOnlyList<ChangedFile>? CompareFiles { get; set; }
        public bool CompareTruncated { get; set; }
        public bool CompareShouldThrow { get; set; }
        public int CompareCalls { get; private set; }

        public Task<Stream> DownloadTarballAsync(
            long installationId, string owner, string repo, string refOrSha, CancellationToken cancellationToken = default) =>
            Task.FromResult(BuildTarballGz(_files));

        public Task<CompareResult> CompareAsync(
            long installationId, string owner, string repo, string baseSha, string headSha, CancellationToken cancellationToken = default)
        {
            CompareCalls++;
            if (CompareShouldThrow)
                throw new InvalidOperationException("base unknown");
            return Task.FromResult(new CompareResult(CompareFiles ?? Array.Empty<ChangedFile>(), CompareTruncated));
        }

        public Task<string> GetDefaultBranchHeadShaAsync(
            long installationId, string owner, string repo, CancellationToken cancellationToken = default) =>
            Task.FromResult("sha-head");
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public int Dimensions => 4;

        public List<string> EmbeddedInputs { get; } = new();

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs, EmbeddingInputType inputType, CancellationToken cancellationToken = default)
        {
            EmbeddedInputs.AddRange(inputs);
            var vectors = inputs.Select(_ => new float[Dimensions]).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        }
    }

    private sealed class FakeCodeChunkRepository : ICodeChunkRepository
    {
        private readonly Dictionary<string, HashSet<string>> _hashesByFile = new();

        public IReadOnlyDictionary<string, HashSet<string>> HashesByFile => _hashesByFile;

        public List<CodeChunkEmbeddingRow> UpsertedRows { get; } = new();

        public void SeedHash(string filePath, string hash)
        {
            if (!_hashesByFile.TryGetValue(filePath, out var set))
                _hashesByFile[filePath] = set = new HashSet<string>();
            set.Add(hash);
        }

        public Task UpsertBatchAsync(
            Guid tenantId, Guid repositoryId, IReadOnlyList<CodeChunkEmbeddingRow> chunks, CancellationToken cancellationToken = default)
        {
            UpsertedRows.AddRange(chunks);
            foreach (var row in chunks)
                SeedHash(row.Candidate.FilePath, row.Candidate.ContentSha256);
            return Task.CompletedTask;
        }

        public Task<int> DeleteByFilePathsAsync(
            Guid tenantId, Guid repositoryId, IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
        {
            var removed = 0;
            foreach (var path in filePaths)
            {
                if (_hashesByFile.Remove(path))
                    removed++;
            }
            return Task.FromResult(removed);
        }

        public Task<int> DeleteExceptFilePathsAsync(
            Guid tenantId, Guid repositoryId, IReadOnlyList<string> keepFilePaths, CancellationToken cancellationToken = default)
        {
            var keep = new HashSet<string>(keepFilePaths, StringComparer.Ordinal);
            var toRemove = _hashesByFile.Keys.Where(k => !keep.Contains(k)).ToList();
            foreach (var path in toRemove)
                _hashesByFile.Remove(path);
            return Task.FromResult(toRemove.Count);
        }

        public Task<int> DeleteStaleHashesAsync(
            Guid tenantId, Guid repositoryId, string filePath, IReadOnlyList<string> keepContentSha256, CancellationToken cancellationToken = default)
        {
            if (!_hashesByFile.TryGetValue(filePath, out var set))
                return Task.FromResult(0);

            var keep = new HashSet<string>(keepContentSha256);
            var toRemove = set.Where(h => !keep.Contains(h)).ToList();
            foreach (var hash in toRemove)
                set.Remove(hash);
            return Task.FromResult(toRemove.Count);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHashesForFilesAsync(
            Guid tenantId, Guid repositoryId, IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, IReadOnlySet<string>>();
            foreach (var path in filePaths)
            {
                if (_hashesByFile.TryGetValue(path, out var set))
                    result[path] = set;
            }
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlySet<string>>>(result);
        }

        public Task<int> CountByRepoAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_hashesByFile.Values.Sum(s => s.Count));

        public Task<IReadOnlyList<VectorHit>> VectorSearchAsync(
            Guid tenantId, IReadOnlyList<Guid> repositoryIds, float[] queryEmbedding, int topK, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VectorHit>>(Array.Empty<VectorHit>());

        public Task<IReadOnlyList<TextHit>> FullTextSearchAsync(
            Guid tenantId, IReadOnlyList<Guid> repositoryIds, string query, int topK, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TextHit>>(Array.Empty<TextHit>());
    }

    private sealed class FakeRepositoryRepository : IRepositoryRepository
    {
        private readonly string? _indexedCommitSha;

        public FakeRepositoryRepository(string? indexedCommitSha) => _indexedCommitSha = indexedCommitSha;

        public Guid? LastSetTenantId { get; private set; }
        public string? LastSetCommitSha { get; private set; }

        public Task<RepositoryLookupRow?> GetByIdAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryLookupRow?>(new RepositoryLookupRow(repositoryId, tenantId, "codist/engineiq", _indexedCommitSha, 42));

        public Task<RepositoryInstallationLookup?> TryResolveByInstallationAndFullNameAsync(
            long installationId, string fullName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetIndexStateAsync(
            Guid tenantId, Guid repositoryId, string commitSha, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
        {
            LastSetTenantId = tenantId;
            LastSetCommitSha = commitSha;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RepositoryLookupRow>> ListIndexedAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryLookupRow>>(Array.Empty<RepositoryLookupRow>());
    }
}
