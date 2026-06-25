namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Deterministic standards rules over PR diffs (in-memory only; no source persistence).
/// </summary>
public interface IStandardsEngine
{
    /// <summary>
    /// Evaluates added diff lines against tenant YAML or the built-in clean-architecture default.
    /// </summary>
    IReadOnlyList<FindingWriteDto> EvaluateDiff(
        string unifiedDiff,
        string? standardsConfigYaml = null,
        Context.RepoContext? repoContext = null);
}
