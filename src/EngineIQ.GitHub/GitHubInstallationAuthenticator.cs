using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Octokit;

namespace EngineIQ.GitHub;

/// <summary>
/// Mints the GitHub App JWT and exchanges it for a short-lived installation token. Shared by
/// <see cref="GitHubAppClient"/> and <see cref="RepoArchiveClient"/> so the private key handling
/// (in memory only, never logged) lives in exactly one place.
/// </summary>
public sealed class GitHubInstallationAuthenticator
{
    private readonly GitHubClientOptions _options;

    public GitHubInstallationAuthenticator(IOptions<GitHubClientOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Creates an Octokit client authenticated as the given installation.</summary>
    public async Task<GitHubClient> GetInstallationClientAsync(long installationId, CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);
        return new GitHubClient(new ProductHeaderValue("EngineIQ"))
        {
            Credentials = new Credentials(token, AuthenticationType.Bearer)
        };
    }

    /// <summary>Exchanges the App JWT for an installation access token (used for the tarball HTTP download).</summary>
    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
    {
        var jwt = CreateJwt();
        var appClient = new GitHubClient(new ProductHeaderValue("EngineIQ"))
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer)
        };
        var token = await appClient.GitHubApps.CreateInstallationToken(installationId);
        return token.Token;
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
}
