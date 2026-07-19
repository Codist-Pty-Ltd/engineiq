using EngineIQ.API.Cors;
using EngineIQ.Domain.Interfaces;

namespace EngineIQ.API.Middleware;

/// <summary>
/// Requires <c>X-Api-Key</c> for <c>/api/v1/*</c> except onboarding register, GitHub install callback, and Jira webhooks.
/// For <c>/api/v1/tenant/{id}/...</c>, the key must belong to that tenant id.
/// Sets <see cref="HttpContext.Items"/> <c>TenantId</c> for per-tenant rate limiting.
/// </summary>
public sealed class ApiKeyTenantMiddleware
{
    private const string TenantIdItemKey = "TenantId";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyTenantMiddleware> _logger;

    public ApiKeyTenantMiddleware(RequestDelegate next, ILogger<ApiKeyTenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantRepository tenants)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (IsOnboardingPublicPath(path, context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Jira Cloud webhooks authenticate via the per-connection secret in the URL path.
        if (path.StartsWithSegments("/api/v1/webhooks/jira", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) || string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            _logger.LogDebug("Missing X-Api-Key for {Path}.", path);
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized);
            return;
        }

        var keyTenantId = await tenants.ValidateApiKeyAndGetTenantIdAsync(apiKeyHeader!, context.RequestAborted);
        if (keyTenantId is null)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized);
            return;
        }

        if (path.StartsWithSegments("/api/v1/tenant", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseTenantRoute(path, out var routeTenantId))
            {
                await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound);
                return;
            }

            if (keyTenantId.Value != routeTenantId)
            {
                _logger.LogWarning("API key tenant mismatch for route {RouteTenantId}.", routeTenantId);
                await WriteJsonErrorAsync(context, StatusCodes.Status403Forbidden);
                return;
            }
        }

        context.Items[TenantIdItemKey] = keyTenantId.Value;
        await _next(context);
    }

    private static bool IsOnboardingPublicPath(PathString path, string method)
    {
        if (!path.StartsWithSegments("/api/v1/onboarding", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Equals("/api/v1/onboarding/register", StringComparison.OrdinalIgnoreCase)
            && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Equals("/api/v1/onboarding/github-callback", StringComparison.OrdinalIgnoreCase)
               && (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseTenantRoute(PathString path, out Guid tenantId)
    {
        tenantId = default;
        var p = path.Value;
        if (string.IsNullOrEmpty(p))
            return false;

        var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return false;
        if (!segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!segments[2].Equals("tenant", StringComparison.OrdinalIgnoreCase))
            return false;
        return Guid.TryParse(segments[3], out tenantId);
    }

    private static async Task WriteJsonErrorAsync(HttpContext context, int statusCode)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await PortalCorsExtensions.ApplyPortalCorsAsync(context);
    }
}
