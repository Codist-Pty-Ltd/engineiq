using System.Text;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.AIEngine;

public static class ReviewPromptBuilder
{
    public static string BuildSystemPrompt(
        TenantPortalPreferences preferences,
        string? standardsConfigYaml,
        RepoContext? repoContext = null)
    {
        var lines = new List<string>
        {
            ReviewService.SystemPrompt.Trim(),
            "",
            "Tenant review preferences:",
        };

        if (preferences.EnforceCursorRules)
        {
            lines.Add("- When a repository includes .cursorrules or similar editor rule files in the diff, treat them as binding guidance for this review.");
        }

        if (preferences.MonetaryTypeSafetyChecks)
        {
            lines.Add("- Flag decimal/double/float types used for money; prefer integer cents for monetary amounts.");
        }

        if (repoContext is not null)
        {
            lines.Add("");
            lines.Add("Repository architecture context (use these real layer folders when discussing layering violations):");
            lines.Add($"- Detected style: {repoContext.DetectedStyle}");
            foreach (var (layer, folders) in repoContext.LayerFolderMap.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                lines.Add($"- {layer}: {string.Join(", ", folders)}");
            }

            foreach (var pattern in repoContext.NotablePatterns)
                lines.Add($"- {pattern}");

            lines.Add("- Reference the actual layer names and folder paths above when flagging architecture issues (e.g. Domain depending on Infrastructure).");
        }

        if (!string.IsNullOrWhiteSpace(standardsConfigYaml))
        {
            lines.Add("");
            lines.Add("Tenant standards YAML (apply rule ids and severities where relevant; do not quote the full YAML in your comment):");
            lines.Add(standardsConfigYaml.Trim());
        }

        return string.Join('\n', lines);
    }
}
