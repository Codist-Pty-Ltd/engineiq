using System.Text.Json;
using System.Text.RegularExpressions;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;

namespace EngineIQ.AIEngine.Anthropic;

/// <summary>
/// Parses Anthropic Messages API JSON responses (assistant text + usage). Testable without HTTP.
/// </summary>
public static class AnthropicReviewResponseParser
{
    /// <summary>PR comment footer linking to the public <c>/security</c> disclosure.</summary>
    public static string BuildTrustFooter(string publicApiBaseUrl)
    {
        var baseUrl = (publicApiBaseUrl ?? "https://api.engineiq.co.za").TrimEnd('/');
        return $"""

---

EngineIQ processed this diff ephemerally. No source code was stored. Findings metadata only is retained for your dashboard. [View our security model]({baseUrl}/security)
""";
    }

    private static readonly Regex FilePathLineRegex = new(
        @"(?<path>[\w./\\-]+\.(?:cs|csproj|ts|tsx|js|jsx|py|md|json|ya?ml|sql|go|rs|java|kt|rb|php|vue|css|scss|html|xml))(?:\:(?<line>\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BacktickPathRegex = new(
        @"`(?<path>[^`\s]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Maps markdown review bullets to persisted finding metadata (no source code in messages).
    /// </summary>
    public static IReadOnlyList<FindingWriteDto> ParseFindingsFromMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return Array.Empty<FindingWriteDto>();

        var body = StripTrustFooter(markdown);
        var findings = new List<FindingWriteDto>();

        foreach (var rawLine in body.Split('\n'))
        {
            if (!TryExtractBulletText(rawLine, out var message) || string.IsNullOrWhiteSpace(message))
                continue;

            var (filePath, lineNumber) = ExtractFileLocation(message);
            var severity = InferSeverity(message);
            var category = InferCategory(message, severity);

            findings.Add(new FindingWriteDto(
                Severity: severity,
                Category: category,
                RuleId: null,
                Source: FindingSources.AI,
                FilePath: filePath,
                LineNumber: lineNumber,
                Message: message.Trim(),
                WasActioned: false,
                PrMergeStatus: "unknown",
                TrainingFeaturesJson: null));
        }

        return findings;
    }

    public static string StripTrustFooter(string markdown)
    {
        var idx = markdown.IndexOf("\n---\n", StringComparison.Ordinal);
        return idx >= 0 ? markdown[..idx] : markdown;
    }

    private static bool TryExtractBulletText(string line, out string message)
    {
        message = string.Empty;
        var t = line.TrimStart();
        if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
        {
            message = t[2..].Trim();
            return true;
        }

        if (t.Length > 2 && char.IsDigit(t[0]))
        {
            var dot = t.IndexOf('.', StringComparison.Ordinal);
            if (dot is > 0 and < 5 && dot < t.Length - 1 && char.IsWhiteSpace(t[dot + 1]))
            {
                message = t[(dot + 1)..].Trim();
                return true;
            }
        }

        return false;
    }

    private static (string FilePath, int? LineNumber) ExtractFileLocation(string message)
    {
        foreach (Match m in FilePathLineRegex.Matches(message))
        {
            var path = m.Groups["path"].Value;
            int? line = m.Groups["line"].Success && int.TryParse(m.Groups["line"].Value, out var n) ? n : null;
            return (path, line);
        }

        foreach (Match m in BacktickPathRegex.Matches(message))
        {
            var path = m.Groups["path"].Value;
            if (path.Contains('.', StringComparison.Ordinal))
                return (path, null);
        }

        return (string.Empty, null);
    }

    private static string InferSeverity(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("critical", StringComparison.Ordinal) || lower.Contains("[critical]", StringComparison.Ordinal))
            return "critical";
        if (lower.Contains("security", StringComparison.Ordinal) || lower.Contains("secret", StringComparison.Ordinal)
            || lower.Contains("vulnerability", StringComparison.Ordinal))
            return "high";
        if (lower.Contains("warning", StringComparison.Ordinal) || lower.Contains("[warn", StringComparison.Ordinal))
            return "warning";
        if (lower.Contains("nit", StringComparison.Ordinal) || lower.Contains("style", StringComparison.Ordinal))
            return "info";
        return "medium";
    }

    private static string InferCategory(string message, string severity)
    {
        var lower = message.ToLowerInvariant();
        if (severity is "critical" or "high" && (lower.Contains("security", StringComparison.Ordinal) || lower.Contains("secret", StringComparison.Ordinal)))
            return "security";
        if (lower.Contains("architecture", StringComparison.Ordinal) || lower.Contains("layering", StringComparison.Ordinal))
            return "architecture";
        if (lower.Contains("async", StringComparison.Ordinal) || lower.Contains("null", StringComparison.Ordinal))
            return "reliability";
        return "general";
    }

    /// <summary>Heuristic count of list-style review bullets (fallback when structured parse yields none).</summary>
    public static int EstimateBulletFindingCount(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return 0;

        markdown = StripTrustFooter(markdown);
        var count = 0;
        foreach (var line in markdown.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            {
                count++;
                continue;
            }

            if (t.Length > 2 && char.IsDigit(t[0]))
            {
                var dot = t.IndexOf('.', StringComparison.Ordinal);
                if (dot is > 0 and < 5 && dot < t.Length - 1 && char.IsWhiteSpace(t[dot + 1]))
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Extracts concatenated text blocks from the assistant message content array.
    /// </summary>
    public static bool TryParseAssistantText(JsonElement root, out string text)
    {
        text = string.Empty;
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return false;

        var sb = new System.Text.StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeEl) &&
                typeEl.GetString() == "text" &&
                block.TryGetProperty("text", out var textEl))
            {
                sb.Append(textEl.GetString());
            }
        }

        text = sb.ToString().Trim();
        return text.Length > 0;
    }

    public static bool TryParseUsage(JsonElement root, out int inputTokens, out int outputTokens)
    {
        inputTokens = 0;
        outputTokens = 0;
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return false;

        if (usage.TryGetProperty("input_tokens", out var inEl) && inEl.TryGetInt32(out var i))
            inputTokens = i;
        if (usage.TryGetProperty("output_tokens", out var outEl) && outEl.TryGetInt32(out var o))
            outputTokens = o;

        return true;
    }

    /// <summary>
    /// Estimates ZAR cost from token counts and USD list prices × FX (for structured logs only).
    /// </summary>
    public static decimal EstimateZarCost(
        int inputTokens,
        int outputTokens,
        double inputUsdPerMillion,
        double outputUsdPerMillion,
        double usdToZar)
    {
        var inputUsd = inputTokens / 1_000_000.0 * inputUsdPerMillion;
        var outputUsd = outputTokens / 1_000_000.0 * outputUsdPerMillion;
        return (decimal)((inputUsd + outputUsd) * usdToZar);
    }
}
