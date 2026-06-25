namespace EngineIQ.ContextBuilder.Architecture;

/// <summary>PR paths that should invalidate cached repo context.</summary>
public static class StructuralChangeDetector
{
    private static readonly string[] StructuralMarkers =
    [
        ".sln",
        ".csproj",
        "Directory.Build.props",
        "Directory.Packages.props",
        "docker-compose",
        "Dockerfile",
        "/Modules/",
        "/modules/",
        "/Features/",
        "/features/",
    ];

    public static bool TouchesStructuralFiles(IReadOnlyList<string> prFilePaths)
    {
        if (prFilePaths.Count == 0)
            return false;

        foreach (var path in prFilePaths)
        {
            var normalized = path.Replace('\\', '/');
            foreach (var marker in StructuralMarkers)
            {
                if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
