using EngineIQ.Domain.Interfaces;

namespace EngineIQ.Domain.Reviews;

/// <summary>AI review output and usage metadata (no diff or raw source persisted).</summary>
public sealed record PrReviewDiffOutcome(
    string CommentBody,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCostZar,
    int FindingsCountEstimate,
    IReadOnlyList<FindingWriteDto> ParsedFindings);
