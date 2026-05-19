using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using EngineIQ.Admin.Options;
using Microsoft.Extensions.Options;

namespace EngineIQ.Admin.Middleware;

public sealed class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AdminAuthOptions _options;

    public BasicAuthMiddleware(RequestDelegate next, IOptions<AdminAuthOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // React SPA under /admin uses session-held Basic credentials for JSON calls only.
        // Static shell is served without HTTP Basic so operators can reach the sign-in page on localhost.
        var path = context.Request.Path;
        // SPA shell + redirects + probes Chrome fires at origin root (e.g. /favicon.ico) must not receive
        // WWW-Authenticate — otherwise the browser shows its native Basic dialog on top of /admin/login.
        if (path.StartsWithSegments("/admin")
            || path == "/"
            || IsAnonymousBrowserProbe(path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var header)
            || !AuthenticationHeaderValue.TryParse(header, out var auth)
            || !string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(auth.Parameter))
        {
            RespondUnauthorized(context);
            return;
        }

        string pair;
        try
        {
            pair = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
        }
        catch
        {
            RespondUnauthorized(context);
            return;
        }

        var sep = pair.IndexOf(':', StringComparison.Ordinal);
        if (sep <= 0)
        {
            RespondUnauthorized(context);
            return;
        }

        var user = pair[..sep];
        var pass = pair[(sep + 1)..];

        var expectedUser = _options.Username ?? string.Empty;
        var expectedPass = _options.Password ?? string.Empty;
        if (!Utf8FixedTimeEquals(user, expectedUser) || !Utf8FixedTimeEquals(pass, expectedPass))
        {
            RespondUnauthorized(context);
            return;
        }

        await _next(context);
    }

    private static bool Utf8FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    /// <summary>
    /// Never send <c>WWW-Authenticate</c> — it triggers the browser-native Basic dialog on incidental requests.
    /// React sign-in uses <c>Authorization</c> on <c>/api/v1/admin</c> only.
    /// </summary>
    private static void RespondUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool IsAnonymousBrowserProbe(PathString path)
    {
        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            return true;
        return path.StartsWithSegments("/.well-known");
    }
}
