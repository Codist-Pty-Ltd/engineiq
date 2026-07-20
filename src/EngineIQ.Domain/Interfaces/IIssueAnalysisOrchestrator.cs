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
    decimal EstimatedCostZar,
    int ReposSearched = 0,
    int ChunksRetrieved = 0);
