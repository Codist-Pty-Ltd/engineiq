using EngineIQ.Domain.Tenants;

namespace EngineIQ.Domain.Reviews;

public sealed record PrReviewJobCommand(
    Guid TenantId,
    long InstallationId,
    string Owner,
    string Repo,
    int PrNumber,
    TenantPortalPreferences Preferences,
    string? StandardsConfigYaml);

public sealed record PrReviewJobResult(
    bool Skipped,
    string? SkipReason,
    PrReviewDiffOutcome? Outcome);
