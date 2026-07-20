using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Search;
using EngineIQ.Domain.Trust;
using EngineIQ.ReviewEngine.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class CommentUpsertTests
{
    [Fact]
    public async Task Existing_comment_calls_update()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var issue = SampleIssue(10, "ENG-10");
        var jira = new FakeJiraClient();
        var analyzed = new FakeAnalyzedIssueRepository
        {
            Existing = new AnalyzedIssueRow(
                Guid.NewGuid(), connectionId, issue.JiraIssueId, issue.IssueKey,
                "c-existing", DateTimeOffset.UtcNow.AddDays(-1), AnalysisTrigger.Created),
        };

        var orchestrator = Build(jira, analyzed);
        var command = Command(tenantId, connectionId, issue);

        await orchestrator.UpsertCommentAsync(command, issue, "body", CancellationToken.None);

        Assert.Equal(1, jira.UpdateCalls);
        Assert.Equal(0, jira.PostCalls);
        Assert.Equal("c-existing", analyzed.LastUpsertedCommentId);
    }

    [Fact]
    public async Task Update_returns_null_falls_back_to_post()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var issue = SampleIssue(11, "ENG-11");
        var jira = new FakeJiraClient { UpdateReturnsNull = true };
        var analyzed = new FakeAnalyzedIssueRepository
        {
            Existing = new AnalyzedIssueRow(
                Guid.NewGuid(), connectionId, issue.JiraIssueId, issue.IssueKey,
                "c-gone", DateTimeOffset.UtcNow.AddDays(-1), AnalysisTrigger.Created),
        };

        var orchestrator = Build(jira, analyzed);
        await orchestrator.UpsertCommentAsync(Command(tenantId, connectionId, issue), issue, "body", CancellationToken.None);

        Assert.Equal(1, jira.UpdateCalls);
        Assert.Equal(1, jira.PostCalls);
        Assert.Equal("c-new", analyzed.LastUpsertedCommentId);
    }

    [Fact]
    public async Task New_issue_posts_and_upserts_analyzed_row()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var issue = SampleIssue(12, "ENG-12");
        var jira = new FakeJiraClient();
        var analyzed = new FakeAnalyzedIssueRepository();

        var orchestrator = Build(jira, analyzed);
        await orchestrator.UpsertCommentAsync(Command(tenantId, connectionId, issue), issue, "body", CancellationToken.None);

        Assert.Equal(0, jira.UpdateCalls);
        Assert.Equal(1, jira.PostCalls);
        Assert.Equal("c-new", analyzed.LastUpsertedCommentId);
        Assert.Equal(issue.IssueKey, analyzed.LastUpsertedIssueKey);
    }

    private static IssueAnalysisOrchestrator Build(FakeJiraClient jira, FakeAnalyzedIssueRepository analyzed) =>
        new(
            jira,
            new NoOpImprovement(),
            new EmptyMappings(),
            new EmptyRepos(),
            new EmptySearch(),
            new EmptyContext(),
            analyzed,
            Options.Create(new TrustOptions()),
            NullLogger<IssueAnalysisOrchestrator>.Instance);

    private static JiraIssueDetails SampleIssue(long id, string key) =>
        new(key, id, "Bug", "Summary", "Desc", "High", "bob", "ENG", DateTimeOffset.UtcNow);

    private static JiraIssueAnalysisJobCommand Command(Guid tenantId, Guid connectionId, JiraIssueDetails issue) =>
        new(tenantId, Guid.NewGuid(), connectionId, issue.IssueKey, issue.JiraIssueId,
            new JiraConnectionInfo("https://x.atlassian.net", "a@b.c", "tok"));

    private sealed class FakeJiraClient : IJiraClient
    {
        public bool UpdateReturnsNull { get; init; }
        public int UpdateCalls { get; private set; }
        public int PostCalls { get; private set; }

        public Task<JiraIssueDetails?> GetIssueAsync(JiraConnectionInfo connection, string issueKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraIssueDetails?>(null);

        public Task<string> PostCommentAsync(JiraConnectionInfo connection, string issueKey, string body, CancellationToken cancellationToken = default)
        {
            PostCalls++;
            return Task.FromResult("c-new");
        }

        public Task<string?> UpdateCommentAsync(
            JiraConnectionInfo connection, string issueKey, string commentId, string body, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(UpdateReturnsNull ? null : commentId);
        }

        public Task<JiraSearchPage> SearchIssuesAsync(
            JiraConnectionInfo connection, string jql, int startAt, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JiraSearchPage(0, startAt, Array.Empty<JiraSearchIssue>()));

        public Task<JiraParentSummary?> GetParentAsync(
            JiraConnectionInfo connection, string parentKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<JiraParentSummary?>(null);
    }

    private sealed class FakeAnalyzedIssueRepository : IAnalyzedIssueRepository
    {
        public AnalyzedIssueRow? Existing { get; init; }
        public string? LastUpsertedCommentId { get; private set; }
        public string? LastUpsertedIssueKey { get; private set; }

        public Task<AnalyzedIssueRow?> GetByIssueAsync(
            Guid tenantId, Guid jiraConnectionId, long jiraIssueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing);

        public Task UpsertAsync(
            Guid tenantId,
            Guid jiraConnectionId,
            long jiraIssueId,
            string issueKey,
            string jiraCommentId,
            DateTimeOffset lastAnalyzedIssueUpdatedAt,
            AnalysisTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            LastUpsertedCommentId = jiraCommentId;
            LastUpsertedIssueKey = issueKey;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpImprovement : IJiraIssueImprovementService
    {
        public Task<(IssueImprovementResult Result, int InputTokens, int OutputTokens, decimal EstimatedCostZar)> ImproveAsync(
            JiraIssueDetails issue,
            CodeSearchResult? codeContext = null,
            Domain.Context.RepoContext? repoContext = null,
            JiraParentSummary? parent = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyMappings : IJiraProjectRepoMappingRepository
    {
        public Task<IReadOnlyList<JiraProjectRepoMappingRow>> ListByConnectionAsync(
            Guid tenantId, Guid jiraConnectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JiraProjectRepoMappingRow>>(Array.Empty<JiraProjectRepoMappingRow>());

        public Task<IReadOnlyList<Guid>> GetRepositoryIdsForProjectAsync(
            Guid tenantId, Guid jiraConnectionId, string projectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task ReplaceAsync(
            Guid tenantId, Guid jiraConnectionId, IReadOnlyList<JiraProjectMappingInput> mappings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptyRepos : IRepositoryRepository
    {
        public Task<RepositoryLookupRow?> GetByIdAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryLookupRow?>(null);

        public Task<RepositoryInstallationLookup?> TryResolveByInstallationAndFullNameAsync(
            long installationId, string fullName, CancellationToken cancellationToken = default) =>
            Task.FromResult<RepositoryInstallationLookup?>(null);

        public Task SetIndexStateAsync(
            Guid tenantId, Guid repositoryId, string commitSha, DateTimeOffset indexedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RepositoryLookupRow>> ListIndexedAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryLookupRow>>(Array.Empty<RepositoryLookupRow>());
    }

    private sealed class EmptySearch : ICodeSearchService
    {
        public Task<CodeSearchResult> SearchAsync(
            Guid tenantId, IReadOnlyList<Guid> repositoryIds, string queryText, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeSearchResult.Empty);
    }

    private sealed class EmptyContext : IContextBuilder
    {
        public Task<Domain.Context.RepoContext?> GetOrBuildAsync(
            Guid tenantId, long installationId, string owner, string repo,
            IReadOnlyList<string> prFilePaths, CancellationToken cancellationToken = default) =>
            Task.FromResult<Domain.Context.RepoContext?>(null);
    }
}
