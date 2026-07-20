using EngineIQ.Domain.Context;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;

namespace EngineIQ.Domain.Interfaces;

public interface IJiraIssueImprovementService
{
    /// <summary>
    /// Improves a Jira issue via a single Claude call. When <paramref name="codeContext"/> has hits,
    /// the prompt includes impact-analysis instructions; otherwise behaviour matches Slice 1.
    /// </summary>
    Task<(IssueImprovementResult Result, int InputTokens, int OutputTokens, decimal EstimatedCostZar)> ImproveAsync(
        JiraIssueDetails issue,
        CodeSearchResult? codeContext = null,
        RepoContext? repoContext = null,
        JiraParentSummary? parent = null,
        CancellationToken cancellationToken = default);
}
