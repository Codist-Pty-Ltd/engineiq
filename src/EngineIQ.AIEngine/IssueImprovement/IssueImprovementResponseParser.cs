using System.Text.Json;
using System.Text.RegularExpressions;
using EngineIQ.Domain.Jira;

namespace EngineIQ.AIEngine.IssueImprovement;

public sealed class IssueImprovementParseException : Exception
{
    public IssueImprovementParseException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Parses Claude JSON for issue improvement (testable without HTTP).</summary>
public static class IssueImprovementResponseParser
{
    private static readonly Regex FenceRegex = new(
        @"^\s*```(?:json)?\s*(.*?)\s*```\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> KnownConfidence = new(StringComparer.OrdinalIgnoreCase)
    {
        "High", "Medium", "Low"
    };

    public static IssueImprovementResult Parse(string assistantText)
    {
        if (string.IsNullOrWhiteSpace(assistantText))
            throw new IssueImprovementParseException("empty_assistant_text");

        var text = assistantText.Trim();
        var fence = FenceRegex.Match(text);
        if (fence.Success)
            text = fence.Groups[1].Value.Trim();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new IssueImprovementParseException("unparseable_json", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new IssueImprovementParseException("expected_json_object");

            var rewritten = ReadString(root, "rewrittenDescription") ?? string.Empty;
            var severity = ReadString(root, "severityAssessment") ?? string.Empty;
            var wellFormed = false;
            if (root.TryGetProperty("isAlreadyWellFormed", out var wf))
            {
                wellFormed = wf.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(wf.GetString(), out var b) && b,
                    _ => false,
                };
            }

            return new IssueImprovementResult(
                rewritten,
                ReadStringArray(root, "acceptanceCriteria"),
                ReadStringArray(root, "missingInfoQuestions"),
                severity,
                wellFormed,
                ReadImpactAnalysis(root));
        }
    }

    private static IssueImpactAnalysis? ReadImpactAnalysis(JsonElement root)
    {
        if (!root.TryGetProperty("impactAnalysis", out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        var files = new List<ImpactedFile>();
        if (el.TryGetProperty("likelyFiles", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var path = SanitizePath(ReadString(item, "path"));
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var reason = ReadString(item, "reason")?.Trim() ?? string.Empty;
                var confidence = NormalizeConfidence(ReadString(item, "confidence"));
                files.Add(new ImpactedFile(path, reason, confidence));
            }
        }

        return new IssueImpactAnalysis(
            files,
            ReadStringArray(el, "affectedModules"),
            ReadString(el, "blastRadius")?.Trim() ?? string.Empty,
            ReadStringArray(el, "suggestedApproach"));
    }

    private static string NormalizeConfidence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Medium";
        var trimmed = value.Trim();
        foreach (var known in KnownConfidence)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return "Medium";
    }

    private static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (path.Contains('`', StringComparison.Ordinal) || path.Contains('\n') || path.Contains('\r'))
            return null;
        return path.Trim();
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrWhiteSpace(s))
                list.Add(s.Trim());
        }

        return list;
    }
}
