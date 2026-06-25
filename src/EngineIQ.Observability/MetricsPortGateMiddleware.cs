using Microsoft.AspNetCore.Http;

namespace EngineIQ.Observability;

/// <summary>Hides /metrics on the public API listener; scrape loopback metrics port only.</summary>
public sealed class MetricsPortGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _metricsPort;

    public MetricsPortGateMiddleware(RequestDelegate next, int metricsPort)
    {
        _next = next;
        _metricsPort = metricsPort;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
            && context.Connection.LocalPort != _metricsPort)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}
