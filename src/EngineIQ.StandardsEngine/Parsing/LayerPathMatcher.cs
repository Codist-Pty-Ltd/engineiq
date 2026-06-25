using EngineIQ.Domain.Context;

namespace EngineIQ.StandardsEngine.Parsing;

public static class LayerPathMatcher
{
    private static readonly IReadOnlyDictionary<string, string[]> DefaultLayerFolders =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Domain"] = ["Domain", "Core"],
            ["Application"] = ["Application", "UseCases"],
            ["Infrastructure"] = ["Infrastructure", "Persistence"],
            ["API"] = ["API", "Controllers", "WebAPI"],
            ["Presentation"] = ["Presentation", "Web", "UI", "Controllers", "WebAPI"],
            ["Business"] = ["Business", "BLL", "Services"],
            ["Data"] = ["Data", "DAL"],
            ["Ports"] = ["Ports", "Port"],
            ["Adapters"] = ["Adapters", "Adapter"],
        };

    public static bool IsInLayer(string path, string layerName, RepoContext? repoContext = null)
    {
        if (repoContext?.LayerFolderMap.TryGetValue(layerName, out var detectedRoots) == true
            && detectedRoots.Count > 0)
        {
            if (PathUnderAnyRoot(path, detectedRoots))
                return true;
        }

        if (!DefaultLayerFolders.TryGetValue(layerName, out var folders))
            return false;

        return PathMatchesFolderTokens(path, folders);
    }

    private static bool PathUnderAnyRoot(string path, IReadOnlyList<string> roots)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var root in roots)
        {
            var r = root.Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith($"{r}/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(r, StringComparison.OrdinalIgnoreCase)
                || normalized.Contains($"/{r}/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathMatchesFolderTokens(string path, IReadOnlyList<string> folders)
    {
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
