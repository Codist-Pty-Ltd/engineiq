using System.Text.RegularExpressions;

namespace EngineIQ.ContextBuilder.Parsing;

public static class DiffPathExtractor
{
    private static readonly Regex GitDiffPathRegex = new(
        @"^diff --git a/(?<path>[^\s]+) b/",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> ExtractFilePaths(string unifiedDiff)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            return Array.Empty<string>();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in GitDiffPathRegex.Matches(unifiedDiff))
        {
            var path = match.Groups["path"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }

        return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
