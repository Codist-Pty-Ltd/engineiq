namespace EngineIQ.Domain.Jira;

public sealed record IssueImprovementResult(
    string RewrittenDescription,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> MissingInfoQuestions,
    string SeverityAssessment,
    bool IsAlreadyWellFormed);
