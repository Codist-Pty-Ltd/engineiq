using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Jira;
using EngineIQ.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class BacklogBackfillConsumerLogicTests
{
    [Fact]
    public async Task Skips_issue_when_analyzed_is_already_current()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var updatedAt = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        var issue = new JiraSearchIssue(100, "ENG-100", "Bug", updatedAt);
        var analyzed = new FakeAnalyzedIssues();
        analyzed.Set(connectionId, issue.Id, new AnalyzedIssueRow(
            Guid.NewGuid(), connectionId, issue.Id, issue.Key, "c1",
            updatedAt.AddMinutes(5),
            AnalysisTrigger.Created));

        var jobs = new FakeIssueJobs();
        var publisher = new CountingPublisher();
        var pacer = new CountingPacer();
        var backfills = new FakeBackfills(Row(jobId, tenantId, connectionId, maxIssues: 10));
        var jira = new FakeJiraSearch(new JiraSearchPage(1, 0, new[] { issue }));

        var consumer = Build(backfills, Connection(tenantId, connectionId), jira, analyzed, jobs, publisher, pacer, delayMs: 0);
        await consumer.RunBackfillAsync(new BacklogBackfillJobMessage(tenantId, jobId, connectionId), CancellationToken.None);

        Assert.Equal(0, publisher.PublishCount);
        Assert.Equal(0, jobs.CreateCalls);
        Assert.Equal(1, backfills.LastSkippedCount);
        Assert.Equal(0, backfills.LastEnqueuedCount);
    }

    [Fact]
    public async Task Invalid_jql_is_marked_failed_via_handler_contract()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var backfills = new FakeBackfills(Row(jobId, tenantId, connectionId));
        var jira = new FakeJiraSearch(throwInvalidJql: true);

        var consumer = Build(
            backfills, Connection(tenantId, connectionId), jira,
            new FakeAnalyzedIssues(), new FakeIssueJobs(), new CountingPublisher(), new CountingPacer(), delayMs: 0);

        var ex = await Assert.ThrowsAsync<InvalidJqlException>(() =>
            consumer.RunBackfillAsync(new BacklogBackfillJobMessage(tenantId, jobId, connectionId), CancellationToken.None));

        // Mirrors HandleMessageAsync catch (InvalidJqlException) → MarkFailedAsync
        await MarkFailedOnInvalidJqlAsync(backfills, tenantId, jobId, ex);

        Assert.Equal(ex.Message, backfills.FailureReason);
        Assert.Equal("failed", backfills.Status);
    }

    [Fact]
    public async Task Pacing_delays_between_published_issues()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        var issues = new[]
        {
            new JiraSearchIssue(1, "ENG-1", "Bug", t0),
            new JiraSearchIssue(2, "ENG-2", "Story", t0),
            new JiraSearchIssue(3, "ENG-3", "Bug", t0),
        };

        var pacer = new CountingPacer();
        var publisher = new CountingPublisher();
        var backfills = new FakeBackfills(Row(jobId, tenantId, connectionId, maxIssues: 10));
        var jira = new FakeJiraSearch(new JiraSearchPage(3, 0, issues));

        var consumer = Build(
            backfills, Connection(tenantId, connectionId), jira,
            new FakeAnalyzedIssues(), new FakeIssueJobs(), publisher, pacer, delayMs: 50);

        await consumer.RunBackfillAsync(new BacklogBackfillJobMessage(tenantId, jobId, connectionId), CancellationToken.None);

        Assert.Equal(3, publisher.PublishCount);
        Assert.Equal(2, pacer.DelayCalls); // first publish has no delay
        Assert.All(pacer.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(50), d));
    }

    /// <summary>Same contract as BacklogBackfillConsumer.HandleMessageAsync InvalidJql catch.</summary>
    private static Task MarkFailedOnInvalidJqlAsync(
        IBacklogBackfillRepository backfills,
        Guid tenantId,
        Guid jobId,
        InvalidJqlException ex) =>
        backfills.MarkFailedAsync(tenantId, jobId, ex.Message, CancellationToken.None);

    private static BacklogBackfillConsumer Build(
        FakeBackfills backfills,
        JiraConnectionRow connection,
        FakeJiraSearch jira,
        FakeAnalyzedIssues analyzed,
        FakeIssueJobs jobs,
        CountingPublisher publisher,
        CountingPacer pacer,
        int delayMs) =>
        new(
            Options.Create(new RabbitMqOptions()),
            Options.Create(new JiraClientOptions { BackfillDelayMs = delayMs }),
            backfills,
            new FakeConnections(connection),
            new PassthroughProtector(),
            jira,
            analyzed,
            jobs,
            publisher,
            pacer,
            NullLogger<BacklogBackfillConsumer>.Instance);

    private static BacklogBackfillRow Row(Guid jobId, Guid tenantId, Guid connectionId, int maxIssues = 100) =>
        new(jobId, tenantId, connectionId, "project = ENG", "processing", 0, 0, 0, 0, maxIssues,
            DateTimeOffset.UtcNow, null, null);

    private static JiraConnectionRow Connection(Guid tenantId, Guid connectionId) =>
        new(connectionId, tenantId, "https://x.atlassian.net", "a@b.c", "tok", "whsec", "ENG", true, "active");

    private sealed class CountingPacer : IBackfillPacer
    {
        public int DelayCalls { get; private set; }
        public List<TimeSpan> Delays { get; } = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCalls++;
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingPublisher : IJiraIssueAnalysisJobPublisher
    {
        public int PublishCount { get; private set; }

        public Task PublishAsync(JiraIssueAnalysisJobMessage job, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJiraSearch : IJiraClient
    {
        private readonly JiraSearchPage _page;
        private readonly bool _throwInvalidJql;

        public FakeJiraSearch(JiraSearchPage? page = null, bool throwInvalidJql = false)
        {
            _page = page ?? new JiraSearchPage(0, 0, Array.Empty<JiraSearchIssue>());
            _throwInvalidJql = throwInvalidJql;
        }

        public Task<JiraIssueDetails?> GetIssueAsync(JiraConnectionInfo connection, string issueKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraIssueDetails?>(null);

        public Task<string> PostCommentAsync(JiraConnectionInfo connection, string issueKey, string body, CancellationToken cancellationToken = default) =>
            Task.FromResult("c");

        public Task<string?> UpdateCommentAsync(
            JiraConnectionInfo connection, string issueKey, string commentId, string body, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(commentId);

        public Task<JiraSearchPage> SearchIssuesAsync(
            JiraConnectionInfo connection, string jql, int startAt, int maxResults, CancellationToken cancellationToken = default)
        {
            if (_throwInvalidJql)
                throw new InvalidJqlException("invalid_jql: bad field");
            return Task.FromResult(_page);
        }

        public Task<JiraParentSummary?> GetParentAsync(
            JiraConnectionInfo connection, string parentKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraParentSummary?>(null);
    }

    private sealed class FakeAnalyzedIssues : IAnalyzedIssueRepository
    {
        private readonly Dictionary<(Guid ConnectionId, long IssueId), AnalyzedIssueRow> _rows = new();

        public void Set(Guid connectionId, long issueId, AnalyzedIssueRow row) =>
            _rows[(connectionId, issueId)] = row;

        public Task<AnalyzedIssueRow?> GetByIssueAsync(
            Guid tenantId, Guid jiraConnectionId, long jiraIssueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.TryGetValue((jiraConnectionId, jiraIssueId), out var row) ? row : null);

        public Task UpsertAsync(
            Guid tenantId, Guid jiraConnectionId, long jiraIssueId, string issueKey, string jiraCommentId,
            DateTimeOffset lastAnalyzedIssueUpdatedAt, AnalysisTrigger trigger, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeIssueJobs : IIssueAnalysisJobRepository
    {
        public int CreateCalls { get; private set; }

        public Task<IssueAnalysisJobEnqueueResult> TryCreateQueuedJobAsync(
            Guid tenantId, Guid jiraConnectionId, string issueKey, long jiraIssueId, string dedupeKey,
            CancellationToken cancellationToken = default, AnalysisTrigger trigger = AnalysisTrigger.Created)
        {
            CreateCalls++;
            return Task.FromResult(new IssueAnalysisJobEnqueueResult(true, tenantId, Guid.NewGuid(), jiraConnectionId));
        }

        public Task<bool> TryMarkJobProcessingIfQueuedAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> MarkJobQueuedAfterPublishAsync(Guid tenantId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<PendingJiraPublishJobInfo>> ListStalePendingPublishJobsAsync(
            TimeSpan staleOlderThan, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PendingJiraPublishJobInfo>>(Array.Empty<PendingJiraPublishJobInfo>());

        public Task MarkJobCompletedAsync(
            Guid tenantId, Guid jobId, long durationMs, int inputTokens, int outputTokens, decimal estimatedCostZar,
            CancellationToken cancellationToken = default, int reposSearched = 0, int chunksRetrieved = 0) =>
            Task.CompletedTask;

        public Task MarkJobFailedAsync(
            Guid tenantId, Guid jobId, string? failureReason, long? durationMs, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkJobSkippedAsync(
            Guid tenantId, Guid jobId, string skipReason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeBackfills : IBacklogBackfillRepository
    {
        private readonly BacklogBackfillRow _row;

        public FakeBackfills(BacklogBackfillRow row) => _row = row;

        public string? FailureReason { get; private set; }
        public string Status { get; private set; } = "processing";
        public int LastSkippedCount { get; private set; }
        public int LastEnqueuedCount { get; private set; }

        public Task<BacklogBackfillEnqueueResult> TryCreateQueuedAsync(
            Guid tenantId, Guid jiraConnectionId, string jql, int maxIssues, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Guid?> FindActiveJobIdAsync(Guid tenantId, Guid jiraConnectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);

        public Task<BacklogBackfillRow?> GetByIdAsync(Guid tenantId, Guid backfillId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BacklogBackfillRow?>(_row);

        public Task<bool> TryMarkProcessingIfQueuedAsync(Guid tenantId, Guid backfillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> MarkQueuedAfterPublishAsync(Guid tenantId, Guid backfillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task UpdateProgressAsync(
            Guid tenantId, Guid backfillId, int startAtCursor, int matchedTotal, int enqueuedCount, int skippedCount,
            CancellationToken cancellationToken = default)
        {
            LastEnqueuedCount = enqueuedCount;
            LastSkippedCount = skippedCount;
            return Task.CompletedTask;
        }

        public Task MarkCompletedAsync(
            Guid tenantId, Guid backfillId, int matchedTotal, int enqueuedCount, int skippedCount,
            CancellationToken cancellationToken = default)
        {
            LastEnqueuedCount = enqueuedCount;
            LastSkippedCount = skippedCount;
            Status = "completed";
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid tenantId, Guid backfillId, string? failureReason, CancellationToken cancellationToken = default)
        {
            FailureReason = failureReason;
            Status = "failed";
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PendingBackfillPublishJobInfo>> ListStalePendingPublishJobsAsync(
            TimeSpan staleOlderThan, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PendingBackfillPublishJobInfo>>(Array.Empty<PendingBackfillPublishJobInfo>());
    }

    private sealed class FakeConnections : IJiraConnectionRepository
    {
        private readonly JiraConnectionRow _row;

        public FakeConnections(JiraConnectionRow row) => _row = row;

        public Task<JiraConnectionRow?> FindByWebhookSecretAsync(string webhookSecret, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraConnectionRow?>(null);

        public Task<JiraConnectionRow?> GetByIdAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraConnectionRow?>(_row);

        public Task<IReadOnlyList<JiraConnectionSummary>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JiraConnectionSummary>>(Array.Empty<JiraConnectionSummary>());

        public Task<JiraConnectionCreated> CreateAsync(
            Guid tenantId, string siteBaseUrl, string email, string apiTokenPlaintext,
            IReadOnlyList<string>? projectKeys, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class PassthroughProtector : IJiraApiTokenProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedPayload) => protectedPayload;
    }
}
