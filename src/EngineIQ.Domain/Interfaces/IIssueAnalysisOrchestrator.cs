using EngineIQ.Domain.Jira;

namespace EngineIQ.Domain.Interfaces;

public interface IIssueAnalysisOrchestrator
{
    Task<IssueAnalysisOutcome> AnalyzeIssueAsync(
        JiraIssueAnalysisJobCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record IssueAnalysisOutcome(
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostZar);
