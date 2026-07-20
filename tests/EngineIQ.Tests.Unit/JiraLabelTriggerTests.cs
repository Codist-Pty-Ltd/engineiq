using EngineIQ.API.Jira;

namespace EngineIQ.Tests.Unit;

public class JiraLabelTriggerTests
{
    private const string Trigger = "engineiq";

    [Fact]
    public void Label_added_returns_true()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "bug", "bug engineiq"),
        };
        Assert.True(JiraWebhookEventFilter.WasTriggerLabelAdded(items, Trigger));
    }

    [Fact]
    public void Label_in_from_and_to_returns_false()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "engineiq bug", "engineiq bug urgent"),
        };
        Assert.False(JiraWebhookEventFilter.WasTriggerLabelAdded(items, Trigger));
    }

    [Fact]
    public void Label_removed_returns_false()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "bug engineiq", "bug"),
        };
        Assert.False(JiraWebhookEventFilter.WasTriggerLabelAdded(items, Trigger));
    }

    [Fact]
    public void Different_label_returns_false()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "bug", "bug needs-review"),
        };
        Assert.False(JiraWebhookEventFilter.WasTriggerLabelAdded(items, Trigger));
    }

    [Fact]
    public void Label_match_is_case_insensitive()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", null, "EngineIQ"),
        };
        Assert.True(JiraWebhookEventFilter.WasTriggerLabelAdded(items, "engineiq"));
        Assert.True(JiraWebhookEventFilter.WasTriggerLabelAdded(items, "ENGINEIQ"));
    }
}
