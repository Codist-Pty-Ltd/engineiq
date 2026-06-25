namespace EngineIQ.StandardsEngine.Parsing;

public static class LayerPathMatcher
{
    private static readonly IReadOnlyDictionary<string, string[]> LayerFolders =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = ["Domain", "Core"],
            ["Application"] = ["Application", "UseCases"],
            ["Infrastructure"] = ["Infrastructure", "Persistence"],
            ["API"] = ["API", "Controllers", "WebAPI"],
        };

    public static bool IsInLayer(string path, string layerName)
    {
        if (!LayerFolders.TryGetValue(layerName, out var folders))
            return false;

        var normalized = path.Replace('\\', '/');
        foreach (var folder in folders)
        {
            if (normalized.Contains($"/{folder}/", StringComparison.Ordinal)
                || normalized.Contains($".{folder}/", StringComparison.Ordinal)
                || normalized.StartsWith($"{folder}/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
