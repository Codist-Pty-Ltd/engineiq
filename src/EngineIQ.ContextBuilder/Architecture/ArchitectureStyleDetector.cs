using System.Text.RegularExpressions;
using EngineIQ.Domain.Context;

namespace EngineIQ.ContextBuilder.Architecture;

/// <summary>Detects architecture style and layer folder map from repository paths.</summary>
public static class ArchitectureStyleDetector
{
    private static readonly (string Style, IReadOnlyDictionary<string, string[]> Layers)[] ScoredStyles =
    [
        (ArchitectureStyles.Clean, LayerFolderCatalog.Clean),
        (ArchitectureStyles.Layered, LayerFolderCatalog.Layered),
        (ArchitectureStyles.Hexagonal, LayerFolderCatalog.Hexagonal),
    ];

    private static readonly Regex ModuleSegmentRegex = new(
        @"(?:^|/)(?:Modules|modules|Features|features)/([^/]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static RepoContext Detect(IReadOnlyList<string> paths)
    {
        var normalized = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var modularScore = ScoreModularMonolith(normalized);
        var (bestStyle, bestScore, bestLayers) = ScoreNamedStyles(normalized);

        if (modularScore > bestScore)
        {
            bestStyle = ArchitectureStyles.ModularMonolith;
            bestLayers = LayerFolderCatalog.ModularMonolith;
            bestScore = modularScore;
        }

        if (bestScore == 0)
            bestStyle = ArchitectureStyles.Unknown;

        var layerMap = BuildLayerFolderMap(normalized, bestLayers);
        var patterns = BuildNotablePatterns(bestStyle, layerMap, normalized);

        return new RepoContext(
            bestStyle,
            layerMap.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal),
            patterns.ToList(),
            DateTimeOffset.UtcNow);
    }

    private static (string Style, int Score, IReadOnlyDictionary<string, string[]> Layers) ScoreNamedStyles(
        IReadOnlyList<string> paths)
    {
        var bestStyle = ArchitectureStyles.Unknown;
        var bestScore = 0;
        IReadOnlyDictionary<string, string[]> bestLayers = LayerFolderCatalog.Clean;

        foreach (var (style, layers) in ScoredStyles)
        {
            var score = ScoreStyle(paths, layers);
            if (score > bestScore)
            {
                bestScore = score;
                bestStyle = style;
                bestLayers = layers;
            }
        }

        return (bestStyle, bestScore, bestLayers);
    }

    private static int ScoreStyle(IReadOnlyList<string> paths, IReadOnlyDictionary<string, string[]> layers)
    {
        var matchedLayers = 0;
        foreach (var (_, tokens) in layers)
        {
            if (paths.Any(path => tokens.Any(token => PathMatchesToken(path, token))))
                matchedLayers++;
        }

        return matchedLayers;
    }

    private static int ScoreModularMonolith(IReadOnlyList<string> paths)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var match = ModuleSegmentRegex.Match(path);
            if (match.Success)
                modules.Add(match.Groups[1].Value);
        }

        if (modules.Count >= 2)
            return modules.Count + 4;

        var srcSiblings = paths
            .Select(p =>
            {
                var idx = p.IndexOf('/', StringComparison.Ordinal);
                return idx > 0 ? p[..idx] : null;
            })
            .Where(s => s is not null)
            .GroupBy(s => s!, StringComparer.OrdinalIgnoreCase)
            .Count();

        return srcSiblings >= 4 ? srcSiblings : 0;
    }

    internal static Dictionary<string, IReadOnlyList<string>> BuildLayerFolderMap(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, string[]> layers)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (layerName, tokens) in layers)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var root = ExtractLayerRoot(path, tokens);
                if (!string.IsNullOrWhiteSpace(root))
                    roots.Add(root);
            }

            if (roots.Count > 0)
                map[layerName] = roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return map;
    }

    internal static bool PathMatchesToken(string path, string token)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(token))
            return false;

        var normalized = path.Replace('\\', '/');
        return normalized.Contains($"/{token}/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains($".{token}/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith($"{token}/", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith($"/{token}", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(token, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ExtractLayerRoot(string path, IReadOnlyList<string> tokens)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            foreach (var token in tokens)
            {
                if (SegmentMatchesToken(parts[i], token))
                    return string.Join('/', parts.Take(i + 1));
            }
        }

        return null;
    }

    private static bool SegmentMatchesToken(string segment, string token) =>
        segment.Equals(token, StringComparison.OrdinalIgnoreCase)
        || segment.EndsWith($".{token}", StringComparison.OrdinalIgnoreCase)
        || segment.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildNotablePatterns(
        string style,
        IReadOnlyDictionary<string, IReadOnlyList<string>> layerMap,
        IReadOnlyList<string> paths)
    {
        var patterns = new List<string>
        {
            $"Detected architecture style: {style}.",
        };

        foreach (var (layer, folders) in layerMap.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            patterns.Add($"{layer} layer folders: {string.Join(", ", folders)}.");
        }

        var moduleMatches = paths
            .Select(p => ModuleSegmentRegex.Match(p))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (moduleMatches.Count >= 2)
            patterns.Add($"Modular boundaries detected: {string.Join(", ", moduleMatches)}.");

        return patterns;
    }
}
