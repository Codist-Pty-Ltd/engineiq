using System.Text.RegularExpressions;

namespace EngineIQ.StandardsEngine.Parsing;

public sealed record DiffHunk(string Path, IReadOnlyList<DiffAddedLine> AddedLines);

public sealed record DiffAddedLine(int LineNumber, string Text);

public static partial class UnifiedDiffParser
{
    [GeneratedRegex(@"\+(\d+)", RegexOptions.Compiled)]
    private static partial Regex HunkStartRegex();

    public static IReadOnlyList<DiffHunk> Parse(string unifiedDiff)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            return Array.Empty<DiffHunk>();

        var hunks = new List<DiffHunk>();
        DiffHunk? current = null;
        var lineNo = 0;

        foreach (var raw in unifiedDiff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var path = line[4..].Trim();
                if (path.StartsWith("b/", StringComparison.Ordinal))
                    path = path[2..];
                current = new DiffHunk(path, []);
                hunks.Add(current);
                continue;
            }

            if (current is null)
                continue;

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var match = HunkStartRegex().Match(line);
                lineNo = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                var added = new List<DiffAddedLine>(current.AddedLines) { new(lineNo, line[1..]) };
                current = current with { AddedLines = added };
                hunks[^1] = current;
                lineNo++;
            }
            else if (line.StartsWith(' ') || line.StartsWith('-'))
            {
                if (!line.StartsWith('-'))
                    lineNo++;
            }
        }

        return hunks;
    }
}
