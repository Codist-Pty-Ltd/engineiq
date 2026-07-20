using EngineIQ.Domain.Interfaces;
using Octokit;
using CompareResult = EngineIQ.Domain.Indexing.CompareResult;
using ChangedFile = EngineIQ.Domain.Indexing.ChangedFile;
using ChangedFileStatus = EngineIQ.Domain.Indexing.ChangedFileStatus;

namespace EngineIQ.GitHub;

/// <summary>
/// GitHub App operations for repository code indexing: tarball snapshots (in memory/temp only) and
/// commit comparisons, authenticated per-installation via <see cref="GitHubInstallationAuthenticator"/>.
/// </summary>
public sealed class RepoArchiveClient : IRepoArchiveClient
{
    /// <summary>GitHub's compare API caps the files array at 300 entries; beyond that we fall back to a full re-index.</summary>
    private const int CompareFileCap = 300;

    private readonly GitHubInstallationAuthenticator _authenticator;

    public RepoArchiveClient(GitHubInstallationAuthenticator authenticator)
    {
        _authenticator = authenticator;
    }

    public async Task<Stream> DownloadTarballAsync(
        long installationId,
        string owner,
        string repo,
        string refOrSha,
        CancellationToken cancellationToken = default)
    {
        var client = await _authenticator.GetInstallationClientAsync(installationId, cancellationToken);
        var bytes = await client.Repository.Content.GetArchive(owner, repo, ArchiveFormat.Tarball, refOrSha);
        return new MemoryStream(bytes, writable: false);
    }

    public async Task<CompareResult> CompareAsync(
        long installationId,
        string owner,
        string repo,
        string baseSha,
        string headSha,
        CancellationToken cancellationToken = default)
    {
        var client = await _authenticator.GetInstallationClientAsync(installationId, cancellationToken);
        var comparison = await client.Repository.Commit.Compare(owner, repo, baseSha, headSha);

        var files = comparison.Files
            .Select(f => new ChangedFile(f.Filename, MapStatus(f.Status), f.PreviousFileName))
            .ToList();

        return new CompareResult(files, comparison.Files.Count >= CompareFileCap);
    }

    public async Task<string> GetDefaultBranchHeadShaAsync(
        long installationId,
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        var client = await _authenticator.GetInstallationClientAsync(installationId, cancellationToken);
        var repository = await client.Repository.Get(owner, repo);
        var branch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;
        var reference = await client.Git.Reference.Get(owner, repo, $"heads/{branch}");
        return reference.Object.Sha;
    }

    private static ChangedFileStatus MapStatus(string status) => status switch
    {
        "added" => ChangedFileStatus.Added,
        "removed" => ChangedFileStatus.Removed,
        "renamed" => ChangedFileStatus.Renamed,
        _ => ChangedFileStatus.Modified,
    };
}
