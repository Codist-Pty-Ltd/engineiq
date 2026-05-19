using Microsoft.AspNetCore.Cors.Infrastructure;

namespace EngineIQ.API.Cors;

internal static class PortalCorsExtensions
{
    public const string PolicyName = "Portal";

    public static async Task ApplyPortalCorsAsync(HttpContext context)
    {
        var cors = context.RequestServices.GetService<ICorsService>();
        var provider = context.RequestServices.GetService<ICorsPolicyProvider>();
        if (cors is null || provider is null)
            return;

        var policy = await provider.GetPolicyAsync(context, PolicyName);
        if (policy is null)
            return;

        var result = cors.EvaluatePolicy(context, policy);
        if (result is not null)
            cors.ApplyResult(result, context.Response);
    }
}
