using EngineIQ.API.Jira;

namespace EngineIQ.Tests.Unit;

public class JiraWebhookControllerTests
{
    [Theory]
    [InlineData("jira:issue_created", "Bug", false, "ENG", null, true)]
    [InlineData("jira:issue_created", "Story", false, "ENG", null, true)]
    [InlineData("jira:issue_created", "bug", false, "ENG", null, true)]
    [InlineData("jira:issue_updated", "Bug", false, "ENG", null, false)]
    [InlineData("jira:issue_created", "Task", false, "ENG", null, false)]
    [InlineData("jira:issue_created", "Bug", true, "ENG", null, false)]
    [InlineData("jira:issue_created", "Bug", false, "OTHER", "ENG,OPS", false)]
    [InlineData("jira:issue_created", "Bug", false, "ENG", "ENG,OPS", true)]
    [InlineData("jira:issue_created", "Bug", false, "ENG", "  eng , ops ", true)]
    public void ShouldEnqueue_applies_event_type_and_project_filters(
        string webhookEvent,
        string issueType,
        bool isSubtask,
        string projectKey,
        string? projectKeysCsv,
        bool expected)
    {
        var ok = JiraWebhookEventFilter.ShouldEnqueue(
            webhookEvent,
            issueType,
            isSubtask,
            projectKey,
            projectKeysCsv,
            out _);
        Assert.Equal(expected, ok);
    }

    [Fact]
    public void ShouldEnqueue_issue_updated_with_label_trigger_enqueues()
    {
        var ok = JiraWebhookEventFilter.ShouldEnqueue(
            "jira:issue_updated",
            "Bug",
            isSubtask: false,
            "ENG",
            null,
            out var skipReason,
            labelTriggerAdded: true);
        Assert.True(ok);
        Assert.Equal(string.Empty, skipReason);
    }

    [Fact]
    public void WasTriggerLabelAdded_detects_new_label()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "bug urgent", "bug urgent engineiq"),
        };
        Assert.True(JiraWebhookEventFilter.WasTriggerLabelAdded(items, "engineiq"));
    }

    [Fact]
    public void WasTriggerLabelAdded_false_when_already_present()
    {
        var items = new[]
        {
            new JiraChangelogLabelItem("labels", "engineiq bug", "engineiq bug urgent"),
        };
        Assert.False(JiraWebhookEventFilter.WasTriggerLabelAdded(items, "engineiq"));
    }

    [Fact]
    public void BuildDedupeKey_joins_issue_id_and_updated()
    {
        Assert.Equal("42:2026-07-19T10:00:00.000+0000", JiraWebhookEventFilter.BuildDedupeKey(42, "2026-07-19T10:00:00.000+0000"));
        Assert.Equal("7:", JiraWebhookEventFilter.BuildDedupeKey(7, null));
    }
}
