using System.Text;
using EiqGitHubClient = EngineIQ.Domain.Interfaces.IGitHubClient;
using GitHubPullRequestInfo = EngineIQ.Domain.Interfaces.GitHubPullRequestInfo;
using Octokit;

namespace EngineIQ.GitHub;

public class GitHubAppClient : EiqGitHubClient
{
    private readonly GitHubInstallationAuthenticator _authenticator;

    public GitHubAppClient(GitHubInstallationAuthenticator authenticator)
    {
        _authenticator = authenticator;
    }

    public async Task<string> GetPullRequestDiffAsync(long installationId, string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
    {
        var client = await GetInstallationClientAsync(installationId, cancellationToken);
        return await client.GetPullRequestDiffAsync(installationId, owner, repo, prNumber, cancellationToken);
    }

    public async Task<GitHubPullRequestInfo> GetPullRequestInfoAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        CancellationToken cancellationToken = default)
    {
        var client = await GetInstallationClientAsync(installationId, cancellationToken);
        return await client.GetPullRequestInfoAsync(installationId, owner, repo, prNumber, cancellationToken);
    }

    public async Task PostReviewCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken cancellationToken = default)
    {
        var client = await GetInstallationClientAsync(installationId, cancellationToken);
        await client.PostReviewCommentAsync(installationId, owner, repo, prNumber, body, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRepositoryFilePathsAsync(
        long installationId,
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        var client = await GetInstallationClientAsync(installationId, cancellationToken);
        return await client.GetRepositoryFilePathsAsync(installationId, owner, repo, cancellationToken);
    }

    private async Task<EiqGitHubClient> GetInstallationClientAsync(long installationId, CancellationToken cancellationToken)
    {
        var installationClient = await _authenticator.GetInstallationClientAsync(installationId, cancellationToken);
        return new InstallationGitHubClient(installationClient);
    }

    private class InstallationGitHubClient : EiqGitHubClient
    {
        private readonly GitHubClient _client;

        public InstallationGitHubClient(GitHubClient client) => _client = client;

        public async Task<GitHubPullRequestInfo> GetPullRequestInfoAsync(
            long installationId,
            string owner,
            string repo,
            int prNumber,
            CancellationToken cancellationToken = default)
        {
            var pr = await _client.PullRequest.Get(owner, repo, prNumber);
            return new GitHubPullRequestInfo(pr.Draft, pr.Title);
        }

        public async Task<string> GetPullRequestDiffAsync(long installationId, string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
        {
            var files = await _client.PullRequest.Files(owner, repo, prNumber);
            var diff = new StringBuilder();
            foreach (var file in files)
                diff.AppendLine(file.Patch ?? $"diff --git a/{file.FileName} b/{file.FileName}\nnew file");
            return diff.ToString();
        }

        public Task PostReviewCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken cancellationToken = default)
            => _client.Issue.Comment.Create(owner, repo, prNumber, body);

        public async Task<IReadOnlyList<string>> GetRepositoryFilePathsAsync(
            long installationId,
            string owner,
            string repo,
            CancellationToken cancellationToken = default)
        {
            var repository = await _client.Repository.Get(owner, repo);
            var branch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;
            var tree = await _client.Git.Tree.GetRecursive(repository.Id, branch);
            return tree.Tree
                .Where(item => item.Type == TreeType.Blob && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path!)
                .ToList();
        }
    }
}
