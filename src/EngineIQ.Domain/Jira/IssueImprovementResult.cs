namespace EngineIQ.Domain.Jira;

public sealed record IssueImprovementResult(
    string RewrittenDescription,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> MissingInfoQuestions,
    string SeverityAssessment,
    bool IsAlreadyWellFormed,
    IssueImpactAnalysis? ImpactAnalysis = null);

/// <summary>Code-impact section produced when hybrid retrieval found relevant chunks.</summary>
public sealed record IssueImpactAnalysis(
    IReadOnlyList<ImpactedFile> LikelyFiles,
    IReadOnlyList<string> AffectedModules,
    string BlastRadius,
    IReadOnlyList<string> SuggestedApproach);

public sealed record ImpactedFile(string Path, string Reason, string Confidence);
