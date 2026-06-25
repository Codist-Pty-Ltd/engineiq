using EngineIQ.Domain.Interfaces;
using EngineIQ.StandardsEngine.Config;
using EngineIQ.StandardsEngine.Parsing;
using EngineIQ.StandardsEngine.Rules;

namespace EngineIQ.StandardsEngine;

/// <summary>
/// Deterministic standards rules over unified PR diffs (M1). Pure in-memory evaluation.
/// </summary>
public sealed class StandardsEngine : IStandardsEngine
{
    public IReadOnlyList<FindingWriteDto> EvaluateDiff(string unifiedDiff, string? standardsConfigYaml = null)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            return Array.Empty<FindingWriteDto>();

        var config = StandardsConfigLoader.Load(standardsConfigYaml);
        var hunks = UnifiedDiffParser.Parse(unifiedDiff);
        return DeterministicRuleExecutor.Execute(hunks, config);
    }
}
