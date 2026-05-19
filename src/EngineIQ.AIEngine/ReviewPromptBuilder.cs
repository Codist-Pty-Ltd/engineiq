using EngineIQ.Domain.Tenants;

namespace EngineIQ.AIEngine;

public static class ReviewPromptBuilder
{
    public static string BuildSystemPrompt(TenantPortalPreferences preferences, string? standardsConfigYaml)
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

        if (!string.IsNullOrWhiteSpace(standardsConfigYaml))
        {
            lines.Add("");
            lines.Add("Tenant standards YAML (apply rule ids and severities where relevant; do not quote the full YAML in your comment):");
            lines.Add(standardsConfigYaml.Trim());
        }

        return string.Join('\n', lines);
    }
}
