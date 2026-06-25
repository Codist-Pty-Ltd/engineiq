using EngineIQ.AIEngine;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Tests.Unit;

public class ReviewPromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_includes_monetary_and_cursorrules_when_enabled()
    {
        var prefs = new TenantPortalPreferences(
            EnforceCursorRules: true,
            MonetaryTypeSafetyChecks: true);
        var prompt = ReviewPromptBuilder.BuildSystemPrompt(prefs, "version: 1\nrules: []");

        Assert.Contains("decimal", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".cursorrules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version: 1", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_includes_repo_context_layers()
    {
        var prefs = new TenantPortalPreferences();
        var context = new RepoContext(
            ArchitectureStyles.Clean,
            new Dictionary<string, List<string>>
            {
                ["Domain"] = ["src/Acme.Domain"],
                ["Infrastructure"] = ["src/Acme.Infrastructure"],
            },
            ["Detected architecture style: clean-architecture."],
            DateTimeOffset.UtcNow);

        var prompt = ReviewPromptBuilder.BuildSystemPrompt(prefs, null, context);

        Assert.Contains("Repository architecture context", prompt, StringComparison.Ordinal);
        Assert.Contains("src/Acme.Domain", prompt, StringComparison.Ordinal);
        Assert.Contains("clean-architecture", prompt, StringComparison.Ordinal);
    }
}
