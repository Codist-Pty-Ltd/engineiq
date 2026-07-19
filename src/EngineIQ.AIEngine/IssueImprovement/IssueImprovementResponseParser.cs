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
                wellFormed);
        }
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
