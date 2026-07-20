namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Maps Jira project keys on a connection to EngineIQ repositories for issue→code impact retrieval.
/// </summary>
public interface IJiraProjectRepoMappingRepository
{
    Task<IReadOnlyList<JiraProjectRepoMappingRow>> ListByConnectionAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Repository ids mapped to <paramref name="projectKey"/> for this connection (may be empty).</summary>
    Task<IReadOnlyList<Guid>> GetRepositoryIdsForProjectAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string projectKey,
        CancellationToken cancellationToken = default);

    /// <summary>Full-replace all mappings for the connection with <paramref name="mappings"/>.</summary>
    Task ReplaceAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        IReadOnlyList<JiraProjectMappingInput> mappings,
        CancellationToken cancellationToken = default);
}

public sealed record JiraProjectRepoMappingRow(
    Guid Id,
    string ProjectKey,
    Guid RepositoryId,
    string RepositoryFullName,
    DateTimeOffset CreatedAt);

public sealed record JiraProjectMappingInput(string ProjectKey, IReadOnlyList<Guid> RepositoryIds);
