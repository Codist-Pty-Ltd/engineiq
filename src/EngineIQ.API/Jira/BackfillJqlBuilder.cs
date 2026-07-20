namespace EngineIQ.API.Jira;

/// <summary>Builds the effective JQL for backlog backfill (testable without HTTP).</summary>
public static class BackfillJqlBuilder
{
    public const string TypeFilter = "issuetype in (Bug, Story)";

    /// <summary>
    /// Returns effective JQL, or null with error when no project keys are available for the default query.
    /// </summary>
    public static string? BuildEffectiveJql(
        string? callerJql,
        string? projectKeysCsv,
        IReadOnlyList<string>? mappedProjectKeys,
        out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(callerJql))
        {
            return $"({callerJql.Trim()}) AND {TypeFilter}";
        }

        var keys = ResolveProjectKeys(projectKeysCsv, mappedProjectKeys);
        if (keys.Count == 0)
        {
            error = "no_project_keys";
            return null;
        }

        var inList = string.Join(", ", keys.Select(k => k.Contains(' ') ? $"\"{k}\"" : k));
        return $"project in ({inList}) AND {TypeFilter} AND statusCategory != Done ORDER BY updated DESC";
    }

    public static IReadOnlyList<string> ResolveProjectKeys(
        string? projectKeysCsv,
        IReadOnlyList<string>? mappedProjectKeys)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            var trimmed = key.Trim().ToUpperInvariant();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        if (!string.IsNullOrWhiteSpace(projectKeysCsv))
        {
            foreach (var part in projectKeysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(part);
        }
        else if (mappedProjectKeys is not null)
        {
            foreach (var key in mappedProjectKeys)
                Add(key);
        }

        return result;
    }
}
