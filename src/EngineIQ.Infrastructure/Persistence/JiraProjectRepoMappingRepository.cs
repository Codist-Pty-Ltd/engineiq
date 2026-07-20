using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class JiraProjectRepoMappingRepository : IJiraProjectRepoMappingRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;

    public JiraProjectRepoMappingRepository(IDbContextFactory<EngineIQDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<JiraProjectRepoMappingRow>> ListByConnectionAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        return await (
            from m in db.JiraProjectRepoMappings.AsNoTracking()
            join r in db.Repositories.AsNoTracking() on m.RepositoryId equals r.Id
            where m.TenantId == tenantId && m.JiraConnectionId == jiraConnectionId
            orderby m.ProjectKey, r.FullName
            select new JiraProjectRepoMappingRow(m.Id, m.ProjectKey, m.RepositoryId, r.FullName, m.CreatedAt)
        ).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetRepositoryIdsForProjectAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        string projectKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            return Array.Empty<Guid>();

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var key = projectKey.Trim().ToUpperInvariant();
        return await db.JiraProjectRepoMappings.AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.JiraConnectionId == jiraConnectionId
                && m.ProjectKey == key)
            .Select(m => m.RepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid tenantId,
        Guid jiraConnectionId,
        IReadOnlyList<JiraProjectMappingInput> mappings,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var connectionExists = await db.JiraConnections.AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == jiraConnectionId, cancellationToken);
        if (!connectionExists)
            throw new InvalidOperationException("jira_connection_not_found");

        await db.JiraProjectRepoMappings
            .Where(m => m.TenantId == tenantId && m.JiraConnectionId == jiraConnectionId)
            .ExecuteDeleteAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var mapping in mappings)
        {
            var key = mapping.ProjectKey?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(key) || mapping.RepositoryIds.Count == 0)
                continue;

            foreach (var repoId in mapping.RepositoryIds.Distinct())
            {
                db.JiraProjectRepoMappings.Add(new JiraProjectRepoMapping
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    JiraConnectionId = jiraConnectionId,
                    ProjectKey = key,
                    RepositoryId = repoId,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
