namespace EngineIQ.Infrastructure.Persistence.Entities;

public sealed class JiraProjectRepoMapping
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JiraConnectionId { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public Guid RepositoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public JiraConnection? JiraConnection { get; set; }
    public Repository? Repository { get; set; }
}
