using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class RepositoryRepository : IRepositoryRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;
    private readonly IOptions<PostgresOptions> _options;

    public RepositoryRepository(IDbContextFactory<EngineIQDbContext> factory, IOptions<PostgresOptions> options)
    {
        _factory = factory;
        _options = options;
    }

    public async Task<RepositoryLookupRow?> GetByIdAsync(
        Guid tenantId,
        Guid repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var installationId = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.GitHubAppInstallationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (installationId is null)
            return null;

        var repo = await db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == repositoryId, cancellationToken);
        if (repo is null)
            return null;

        return new RepositoryLookupRow(repo.Id, repo.TenantId, repo.FullName, repo.IndexedCommitSha, installationId.Value);
    }

    public async Task<RepositoryInstallationLookup?> TryResolveByInstallationAndFullNameAsync(
        long installationId,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveTenantIdByInstallationAsync(installationId, cancellationToken);
        if (tenantId is null)
            return null;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId.Value, cancellationToken);

        var tenantStatus = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId.Value)
            .Select(t => t.Status)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        var repository = await db.Repositories
            .FirstOrDefaultAsync(r => r.TenantId == tenantId.Value && r.FullName == fullName, cancellationToken);

        if (repository is null)
        {
            repository = new Repository
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                FullName = fullName,
            };
            db.Repositories.Add(repository);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                repository = await db.Repositories
                    .FirstAsync(r => r.TenantId == tenantId.Value && r.FullName == fullName, cancellationToken);
            }
        }

        return new RepositoryInstallationLookup(tenantId.Value, repository.Id, repository.IndexedCommitSha, tenantStatus);
    }

    public async Task SetIndexStateAsync(
        Guid tenantId,
        Guid repositoryId,
        string commitSha,
        DateTimeOffset indexedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == repositoryId, cancellationToken);
        if (repo is null)
            return;
        repo.IndexedCommitSha = commitSha;
        repo.IndexedAt = indexedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RepositoryLookupRow>> ListIndexedAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);

        var rows = await (
            from r in db.Repositories.AsNoTracking()
            join t in db.Tenants.AsNoTracking() on r.TenantId equals t.Id
            where r.TenantId == tenantId && r.IndexedCommitSha != null && r.IndexedCommitSha != ""
            select new { r.Id, r.TenantId, r.FullName, r.IndexedCommitSha, InstallationId = t.GitHubAppInstallationId }
        ).ToListAsync(cancellationToken);

        return rows
            .Where(r => r.InstallationId.HasValue)
            .Select(r => new RepositoryLookupRow(r.Id, r.TenantId, r.FullName, r.IndexedCommitSha, r.InstallationId!.Value))
            .ToList();
    }

    private async Task<Guid?> ResolveTenantIdByInstallationAsync(long installationId, CancellationToken cancellationToken)
    {
        var cs = _options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Postgres:ConnectionString is not configured.");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("SELECT public.fn_resolve_tenant_by_installation(@i)", conn);
        cmd.Parameters.AddWithValue("i", installationId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid g ? g : null;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
