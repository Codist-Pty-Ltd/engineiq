using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Interfaces;

/// <summary>
/// Anthropic Claude integration for PR review. Returns comment body and usage metadata.
/// </summary>
public interface IAIEngine
{
    /// <summary>
    /// Reviews a PR diff in memory and returns the PR comment body (markdown) plus token/cost metadata.
    /// </summary>
    Task<PrReviewDiffOutcome> ReviewDiffAsync(
        string diff,
        TenantPortalPreferences? preferences = null,
        string? standardsConfigYaml = null,
        CancellationToken cancellationToken = default);
}
