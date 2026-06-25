using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using EiqGitHubClient = EngineIQ.Domain.Interfaces.IGitHubClient;
using GitHubPullRequestInfo = EngineIQ.Domain.Interfaces.GitHubPullRequestInfo;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Octokit;

namespace EngineIQ.GitHub;

public class GitHubAppClient : EiqGitHubClient
{
    private readonly GitHubClientOptions _options;

    public GitHubAppClient(IOptions<GitHubClientOptions> options)
    {
        _options = options.Value;
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
        var jwt = CreateJwt();
        var appClient = new GitHubClient(new ProductHeaderValue("EngineIQ"))
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer)
        };
        var token = await appClient.GitHubApps.CreateInstallationToken(installationId);
        var installationClient = new GitHubClient(new ProductHeaderValue("EngineIQ"))
        {
            Credentials = new Credentials(token.Token, AuthenticationType.Bearer)
        };
        return new InstallationGitHubClient(installationClient);
    }

    private string CreateJwt()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PrivateKeyPem);
        var key = new RsaSecurityKey(rsa.ExportParameters(true));
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.AppId.ToString(),
            IssuedAt = now.AddSeconds(-60),
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
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
