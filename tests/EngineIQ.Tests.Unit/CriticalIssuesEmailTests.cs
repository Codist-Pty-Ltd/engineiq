using EngineIQ.AIEngine;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Notifications;
using EngineIQ.Domain.Persistence;
using EngineIQ.Domain.Tenants;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIQ.Tests.Unit;

public class CriticalIssuesEmailTests
{
    private static FindingWriteDto CriticalFinding(string message = "Critical: SQL injection risk") =>
        new("critical", "security", null, FindingSources.AI, "src/Api.cs", 10, message, false, "unknown", null);

    private static FindingWriteDto HighFinding() =>
        new("high", "security", null, FindingSources.AI, "src/Api.cs", 11, "High severity issue", false, "unknown", null);

    private static readonly TenantPortalPreferences EmailOn = new(EmailOnCriticalIssues: true);
    private static readonly TenantPortalPreferences EmailOff = new(EmailOnCriticalIssues: false);

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void ShouldNotify_requires_critical_finding_preference_and_sendgrid(
        bool hasCritical,
        bool preferenceOn,
        bool sendGridConfigured,
        bool expected)
    {
        var findings = hasCritical
            ? new[] { CriticalFinding(), HighFinding() }
            : new[] { HighFinding() };
        var prefs = preferenceOn ? EmailOn : EmailOff;

        Assert.Equal(expected, CriticalIssuesNotificationPolicy.ShouldNotify(findings, prefs, sendGridConfigured));
    }

    private sealed class RecordingEmailService : IEmailNotificationService
    {
        public int CriticalCallCount { get; private set; }
        public CriticalIssuesEmailRequest? LastRequest { get; private set; }

        public Task SendWelcomeWithInstallLinkAsync(
            string toEmail,
            string companyName,
            string installUrl,
            string dpaPdfUrl,
            string webhookSecretPlaintext,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendLiveConfirmationAsync(string toEmail, string dashboardUrl, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendThirtyDayReportAsync(string toEmail, object templateData, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendCriticalIssuesNotificationAsync(
            CriticalIssuesEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            CriticalCallCount++;
            LastRequest = request;
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenantRepository : ITenantRepository
    {
        private readonly TenantAccountSnapshot? _account;

        public StubTenantRepository(TenantAccountSnapshot? account) => _account = account;

        public Task<RegisterTenantResult> RegisterAsync(RegisterTenantCommand command, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Guid?> ValidateApiKeyAndGetTenantIdAsync(string apiKey, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(bool Ok, string? ApiKeyPlaintext)> RotateApiKeyAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(bool Ok, Guid? TenantId, string? ContactEmail, string? Error)> CompleteGitHubInstallAsync(
            long installationId,
            string installState,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<TenantStatusSnapshot?> GetStatusSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<TenantAccountSnapshot?> GetAccountSnapshotAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_account);

        public Task<TenantDashboardAnalytics?> GetDashboardAnalyticsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(bool Ok, string? InstallState, string? Error)> EnsureGitHubInstallStateAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task UpdateConfigYamlAsync(Guid tenantId, string? yaml, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<TenantPortalPreferences?> GetPortalPreferencesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<TenantPortalPreferences?> UpdatePortalPreferencesAsync(
            Guid tenantId,
            TenantPortalPreferencesPatch patch,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<string?> GetConfigYamlAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<TenantRepositoryRow>> ListRepositoriesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private static PullReviewJobMessage SampleJob() =>
        new(
            Guid.Parse("e2a5cfb5-5148-4737-9ee2-0c0f4d2093bf"),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            12345,
            "codist-pty-ltd",
            "engineiq",
            42,
            0);

    [Fact]
    public async Task TryNotifyAsync_sends_email_when_critical_preference_and_sendgrid_configured()
    {
        var email = new RecordingEmailService();
        var tenants = new StubTenantRepository(new TenantAccountSnapshot(
            SampleJob().TenantId,
            "Codist Pty Ltd",
            "Starter",
            "Active",
            "cto@company.co.za",
            "codist-pty-ltd",
            true,
            12345,
            false));

        await ReviewCriticalIssuesEmailNotifier.TryNotifyAsync(
            email,
            tenants,
            "https://app.engineiq.co.za",
            SampleJob(),
            new[] { CriticalFinding("Critical auth bypass"), HighFinding() },
            EmailOn,
            sendGridConfigured: true,
            NullLogger.Instance);

        Assert.Equal(1, email.CriticalCallCount);
        Assert.NotNull(email.LastRequest);
        Assert.Equal("cto@company.co.za", email.LastRequest!.ToEmail);
        Assert.Equal("codist-pty-ltd/engineiq", email.LastRequest.RepositoryFullName);
        Assert.Equal(42, email.LastRequest.PrNumber);
        Assert.Equal(1, email.LastRequest.CriticalCount);
        Assert.Single(email.LastRequest.FindingMessages);
        Assert.Contains("/dashboard/reviews?job=", email.LastRequest.JobDetailUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryNotifyAsync_skips_when_preference_off_or_no_critical(bool preferenceOn)
    {
        var email = new RecordingEmailService();
        var tenants = new StubTenantRepository(null);
        var findings = preferenceOn
            ? new[] { HighFinding() }
            : new[] { CriticalFinding() };

        await ReviewCriticalIssuesEmailNotifier.TryNotifyAsync(
            email,
            tenants,
            "https://app.engineiq.co.za",
            SampleJob(),
            findings,
            preferenceOn ? EmailOn : EmailOff,
            sendGridConfigured: true,
            NullLogger.Instance);

        Assert.Equal(0, email.CriticalCallCount);
    }

    [Fact]
    public async Task TryNotifyAsync_skips_when_sendgrid_not_configured()
    {
        var email = new RecordingEmailService();
        var tenants = new StubTenantRepository(null);

        await ReviewCriticalIssuesEmailNotifier.TryNotifyAsync(
            email,
            tenants,
            "https://app.engineiq.co.za",
            SampleJob(),
            new[] { CriticalFinding() },
            EmailOn,
            sendGridConfigured: false,
            NullLogger.Instance);

        Assert.Equal(0, email.CriticalCallCount);
    }
}
