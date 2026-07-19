using EngineIQ.Domain.Jira;

namespace EngineIQ.Domain.Interfaces;

public interface IJiraIssueImprovementService
{
    Task<(IssueImprovementResult Result, int InputTokens, int OutputTokens, decimal EstimatedCostZar)> ImproveAsync(
        JiraIssueDetails issue,
        CancellationToken cancellationToken = default);
}
