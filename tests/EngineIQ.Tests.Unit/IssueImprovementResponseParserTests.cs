using EngineIQ.AIEngine.IssueImprovement;
using EngineIQ.Domain.Jira;

namespace EngineIQ.Tests.Unit;

public class IssueImprovementResponseParserTests
{
    [Fact]
    public void Parse_reads_well_formed_json_object()
    {
        const string json = """
            {
              "rewrittenDescription": "As a user I want login",
              "acceptanceCriteria": ["Given login When submit Then success"],
              "missingInfoQuestions": ["Who is the actor?"],
              "severityAssessment": "Medium — UX impact",
              "isAlreadyWellFormed": false
            }
            """;

        var result = IssueImprovementResponseParser.Parse(json);

        Assert.Equal("As a user I want login", result.RewrittenDescription);
        Assert.Single(result.AcceptanceCriteria);
        Assert.Single(result.MissingInfoQuestions);
        Assert.Contains("Medium", result.SeverityAssessment);
        Assert.False(result.IsAlreadyWellFormed);
    }

    [Fact]
    public void Parse_strips_markdown_fences()
    {
        const string fenced = """
            ```json
            {
              "rewrittenDescription": "Bug report",
              "acceptanceCriteria": [],
              "missingInfoQuestions": [],
              "severityAssessment": "High",
              "isAlreadyWellFormed": true
            }
            ```
            """;

        var result = IssueImprovementResponseParser.Parse(fenced);
        Assert.True(result.IsAlreadyWellFormed);
        Assert.Equal("Bug report", result.RewrittenDescription);
    }

    [Fact]
    public void Parse_throws_on_empty_text()
    {
        Assert.Throws<IssueImprovementParseException>(() => IssueImprovementResponseParser.Parse("  "));
    }

    [Fact]
    public void Parse_throws_on_invalid_json()
    {
        Assert.Throws<IssueImprovementParseException>(() => IssueImprovementResponseParser.Parse("not json"));
    }
}
