using System.Text;
using System.Text.RegularExpressions;

namespace EngineIQ.Domain.Search;

/// <summary>
/// Builds Postgres <c>simple</c>-config tsquery fragments from issue text. Identifier tokens are
/// OR-ed so CamelCase symbols in titles hit chunks even when surrounding prose differs.
/// </summary>
public static class CodeSearchQueryTokens
{
    private static readonly Regex IdentifierRegex = new(
        @"[A-Za-z_][A-Za-z0-9_]{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Extracts identifier-like tokens (≥4 chars) from free text.</summary>
    public static IReadOnlyList<string> ExtractIdentifiers(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (Match m in IdentifierRegex.Matches(query))
        {
            var token = SanitizeTsQueryToken(m.Value);
            if (token.Length < 4)
                continue;
            if (seen.Add(token))
                list.Add(token);
        }

        return list;
    }

    /// <summary>
    /// Builds an OR-joined <c>to_tsquery('simple', ...)</c> fragment from identifiers, or null when none.
    /// Tokens are sanitized so hostile characters never reach Postgres.
    /// </summary>
    public static string? BuildIdentifierTsQuery(string? query)
    {
        var tokens = ExtractIdentifiers(query);
        if (tokens.Count == 0)
            return null;

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
                sb.Append(" | ");
            sb.Append(tokens[i]);
        }

        return sb.ToString();
    }

    /// <summary>Strips characters that are special in <c>to_tsquery</c> / <c>plainto_tsquery</c>.</summary>
    public static string SanitizeTsQueryToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;

        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
