using Microsoft.Extensions.Logging;

namespace EngineIQ.Observability;

/// <summary>Structured logging scope with consistent review correlation fields.</summary>
public static class ReviewLogScope
{
    public static IDisposable Begin(
        ILogger logger,
        Guid tenantId,
        Guid jobId,
        int prNumber,
        string? owner = null,
        string? repo = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["tenant_id"] = tenantId,
            ["job_id"] = jobId,
            ["pr_number"] = prNumber,
        };

        if (!string.IsNullOrWhiteSpace(owner))
            scope["owner"] = owner;
        if (!string.IsNullOrWhiteSpace(repo))
            scope["repo"] = repo;

        return logger.BeginScope(scope)!;
    }
}
