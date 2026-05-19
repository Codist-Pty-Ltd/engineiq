using EngineIQ.AIEngine;
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
}
