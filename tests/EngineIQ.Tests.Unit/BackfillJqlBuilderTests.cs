using EngineIQ.API.Jira;

namespace EngineIQ.Tests.Unit;

public class BackfillJqlBuilderTests
{
    [Fact]
    public void Default_jql_from_connection_keys()
    {
        var jql = BackfillJqlBuilder.BuildEffectiveJql(
            callerJql: null,
            projectKeysCsv: "ENG, OPS",
            mappedProjectKeys: null,
            out var error);

        Assert.Null(error);
        Assert.Equal(
            "project in (ENG, OPS) AND issuetype in (Bug, Story) AND statusCategory != Done ORDER BY updated DESC",
            jql);
    }

    [Fact]
    public void Default_jql_from_mapped_keys_when_csv_null()
    {
        var jql = BackfillJqlBuilder.BuildEffectiveJql(
            callerJql: null,
            projectKeysCsv: null,
            mappedProjectKeys: new[] { "mb", "OPS" },
            out var error);

        Assert.Null(error);
        Assert.Equal(
            "project in (MB, OPS) AND issuetype in (Bug, Story) AND statusCategory != Done ORDER BY updated DESC",
            jql);
    }

    [Fact]
    public void Caller_jql_wrapped_with_type_filter()
    {
        var jql = BackfillJqlBuilder.BuildEffectiveJql(
            callerJql: "project = ENG AND labels = backlog",
            projectKeysCsv: null,
            mappedProjectKeys: null,
            out var error);

        Assert.Null(error);
        Assert.Equal(
            "(project = ENG AND labels = backlog) AND issuetype in (Bug, Story)",
            jql);
    }

    [Fact]
    public void No_keys_returns_null_and_no_project_keys_error()
    {
        var jql = BackfillJqlBuilder.BuildEffectiveJql(
            callerJql: null,
            projectKeysCsv: null,
            mappedProjectKeys: Array.Empty<string>(),
            out var error);

        Assert.Null(jql);
        Assert.Equal("no_project_keys", error);
    }
}
