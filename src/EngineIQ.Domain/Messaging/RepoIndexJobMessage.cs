namespace EngineIQ.Domain.Messaging;

/// <summary>
/// Queue payload for repository code-index jobs. No source code in the message.
/// </summary>
public sealed record RepoIndexJobMessage(
    Guid TenantId,
    Guid JobId,
    Guid RepositoryId,
    long InstallationId,
    string Owner,
    string Repo,
    string HeadSha,
    string? BaseSha,
    int Attempt = 0);
