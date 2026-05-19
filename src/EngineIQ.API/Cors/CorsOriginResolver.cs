namespace EngineIQ.API.Cors;

internal static class CorsOriginResolver
{
    /// <summary>
    /// Merges <c>Cors:AllowedOrigins</c> (and <c>Cors__AllowedOrigins__N</c>) with optional
    /// <c>CORS_ALLOWED_ORIGINS</c> comma-separated env for production overrides.
    /// </summary>
    public static string[] Resolve(IConfiguration configuration)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var fromSection = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (fromSection is { Length: > 0 })
        {
            foreach (var o in fromSection)
                AddOrigin(origins, o);
        }

        var csv = configuration["CORS_ALLOWED_ORIGINS"];
        if (!string.IsNullOrWhiteSpace(csv))
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddOrigin(origins, part);
        }

        if (origins.Count == 0)
        {
            origins.Add("http://localhost:3000");
            origins.Add("http://localhost:3001");
        }

        return origins.ToArray();
    }

    private static void AddOrigin(ISet<string> set, string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return;
        set.Add(origin.Trim().TrimEnd('/'));
    }
}
