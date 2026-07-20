using EngineIQ.AIEngine.IssueImprovement;

namespace EngineIQ.Tests.Unit;

public class IssueImprovementResponseParserSlice2bTests
{
    [Fact]
    public void Parse_with_impactAnalysis()
    {
        const string json = """
            {
              "rewrittenDescription": "desc",
              "acceptanceCriteria": ["ac"],
              "missingInfoQuestions": [],
              "severityAssessment": "High",
              "isAlreadyWellFormed": false,
              "impactAnalysis": {
                "likelyFiles": [
                  { "path": "src/InvoiceService.cs", "reason": "handles save", "confidence": "High" }
                ],
                "affectedModules": ["Billing"],
                "blastRadius": "unit tests for InvoiceService",
                "suggestedApproach": ["Locate Save", "Add guard"]
              }
            }
            """;

        var result = IssueImprovementResponseParser.Parse(json);
        Assert.NotNull(result.ImpactAnalysis);
        Assert.Single(result.ImpactAnalysis!.LikelyFiles);
        Assert.Equal("src/InvoiceService.cs", result.ImpactAnalysis.LikelyFiles[0].Path);
        Assert.Equal("High", result.ImpactAnalysis.LikelyFiles[0].Confidence);
        Assert.Contains("Billing", result.ImpactAnalysis.AffectedModules);
        Assert.Equal(2, result.ImpactAnalysis.SuggestedApproach.Count);
    }

    [Fact]
    public void Parse_slice1_shape_without_impact()
    {
        const string json = """
            {
              "rewrittenDescription": "desc",
              "acceptanceCriteria": [],
              "missingInfoQuestions": [],
              "severityAssessment": "Low",
              "isAlreadyWellFormed": true
            }
            """;

        var result = IssueImprovementResponseParser.Parse(json);
        Assert.Null(result.ImpactAnalysis);
        Assert.True(result.IsAlreadyWellFormed);
    }

    [Fact]
    public void Parse_unknown_confidence_becomes_Medium()
    {
        const string json = """
            {
              "rewrittenDescription": "d",
              "acceptanceCriteria": [],
              "missingInfoQuestions": [],
              "severityAssessment": "x",
              "isAlreadyWellFormed": false,
              "impactAnalysis": {
                "likelyFiles": [{ "path": "a.cs", "reason": "r", "confidence": "SuperHigh" }],
                "affectedModules": [],
                "blastRadius": "",
                "suggestedApproach": []
              }
            }
            """;

        var result = IssueImprovementResponseParser.Parse(json);
        Assert.Equal("Medium", result.ImpactAnalysis!.LikelyFiles[0].Confidence);
    }

    [Fact]
    public void Parse_rejects_paths_with_backticks_or_newlines()
    {
        const string json = """
            {
              "rewrittenDescription": "d",
              "acceptanceCriteria": [],
              "missingInfoQuestions": [],
              "severityAssessment": "x",
              "isAlreadyWellFormed": false,
              "impactAnalysis": {
                "likelyFiles": [
                  { "path": "good.cs", "reason": "ok", "confidence": "Low" },
                  { "path": "bad`path.cs", "reason": "x", "confidence": "High" },
                  { "path": "line\nbreak.cs", "reason": "x", "confidence": "High" }
                ],
                "affectedModules": [],
                "blastRadius": "",
                "suggestedApproach": []
              }
            }
            """;

        var result = IssueImprovementResponseParser.Parse(json);
        Assert.Single(result.ImpactAnalysis!.LikelyFiles);
        Assert.Equal("good.cs", result.ImpactAnalysis.LikelyFiles[0].Path);
    }
}
