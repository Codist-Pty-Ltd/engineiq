using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;
using EngineIQ.Domain.Trust;
using EngineIQ.ReviewEngine.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class IssueAnalysisOrchestratorResolutionTests
{
    [Fact]
    public async Task ResolveTargetRepos_explicit_mapping_wins()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var mapped = Guid.NewGuid();
        var other = Guid.NewGuid();

        var mappings = new FakeMappings(new Dictionary<string, IReadOnlyList<Guid>>
        {
            ["MB"] = new[] { mapped },
        });
        var repos = new FakeRepos(new[]
        {
            Row(mapped, tenantId, "org/mapped"),
            Row(other, tenantId, "org/other"),
        });

        var orchestrator = Build(mappings, repos);
        var command = new JiraIssueAnalysisJobCommand(
            tenantId, Guid.NewGuid(), connectionId, "MB-1", 1,
            new JiraConnectionInfo("https://x.atlassian.net", "a@b.c", "tok"));

        var resolved = await orchestrator.ResolveTargetReposAsync(command, "MB", CancellationToken.None);
        Assert.Single(resolved);
        Assert.Equal(mapped, resolved[0].Id);
    }

    [Fact]
    public async Task ResolveTargetRepos_fallback_is_only_indexed_repos()
    {
        var tenantId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var indexed = Guid.NewGuid();

        var mappings = new FakeMappings(new Dictionary<string, IReadOnlyList<Guid>>());
        var repos = new FakeRepos(new[] { Row(indexed, tenantId, "org/indexed") });

        var orchestrator = Build(mappings, repos);
        var command = new JiraIssueAnalysisJobCommand(
            tenantId, Guid.NewGuid(), connectionId, "ENG-1", 1,
            new JiraConnectionInfo("https://x.atlassian.net", "a@b.c", "tok"));

        var resolved = await orchestrator.ResolveTargetReposAsync(command, "ENG", CancellationToken.None);
        Assert.Single(resolved);
        Assert.Equal(indexed, resolved[0].Id);
    }

    [Fact]
    public async Task ResolveTargetRepos_nothing_indexed_returns_empty()
    {
        var tenantId = Guid.NewGuid();
        var mappings = new FakeMappings(new Dictionary<string, IReadOnlyList<Guid>>());
        var repos = new FakeRepos(Array.Empty<RepositoryLookupRow>());
        var orchestrator = Build(mappings, repos);
        var command = new JiraIssueAnalysisJobCommand(
            tenantId, Guid.NewGuid(), Guid.NewGuid(), "ENG-1", 1,
            new JiraConnectionInfo("https://x.atlassian.net", "a@b.c", "tok"));

        var resolved = await orchestrator.ResolveTargetReposAsync(command, "ENG", CancellationToken.None);
        Assert.Empty(resolved);
    }

    [Fact]
    public async Task AnalyzeIssue_search_throwing_still_posts_slice1_comment()
    {
        var tenantId = Guid.NewGuid();
        var repoId = Guid.NewGuid();
        var issue = new JiraIssueDetails(
            "ENG-9", 9, "Bug", "Summary", "Desc", "High", "bob", "ENG", null);

        var jira = new FakeJiraClient(issue);
        var improvement = new FakeImprovement();
        var mappings = new FakeMappings(new Dictionary<string, IReadOnlyList<Guid>>());
        var repos = new FakeRepos(new[] { Row(repoId, tenantId, "org/repo") });
        var search = new ThrowingSearch();
        var context = new FakeContextBuilder();

        var orchestrator = new IssueAnalysisOrchestrator(
            jira,
            improvement,
            mappings,
            repos,
            search,
            context,
            Options.Create(new TrustOptions { PublicApiBaseUrl = "https://api.test" }),
            NullLogger<IssueAnalysisOrchestrator>.Instance);

        var outcome = await orchestrator.AnalyzeIssueAsync(
            new JiraIssueAnalysisJobCommand(
                tenantId, Guid.NewGuid(), Guid.NewGuid(), "ENG-9", 9,
                new JiraConnectionInfo("https://x.atlassian.net", "a@b.c", "tok")));

        Assert.NotNull(jira.PostedComment);
        Assert.DoesNotContain("Impact Analysis", jira.PostedComment);
        Assert.Null(improvement.LastCodeContext?.Hits.FirstOrDefault());
        Assert.Equal(1, outcome.ReposSearched);
        Assert.Equal(0, outcome.ChunksRetrieved);
    }

    private static IssueAnalysisOrchestrator Build(
        IJiraProjectRepoMappingRepository mappings,
        IRepositoryRepository repos) =>
        new(
            new FakeJiraClient(null!),
            new FakeImprovement(),
            mappings,
            repos,
            new EmptySearch(),
            new FakeContextBuilder(),
            Options.Create(new TrustOptions()),
            NullLogger<IssueAnalysisOrchestrator>.Instance);

    private static RepositoryLookupRow Row(Guid id, Guid tenantId, string fullName) =>
        new(id, tenantId, fullName, "sha", 42);

    private sealed class FakeMappings : IJiraProjectRepoMappingRepository
    {
        private readonly Dictionary<string, IReadOnlyList<Guid>> _byProject;

        public FakeMappings(Dictionary<string, IReadOnlyList<Guid>> byProject) => _byProject = byProject;

        public Task<IReadOnlyList<JiraProjectRepoMappingRow>> ListByConnectionAsync(
            Guid tenantId, Guid jiraConnectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JiraProjectRepoMappingRow>>(Array.Empty<JiraProjectRepoMappingRow>());

        public Task<IReadOnlyList<Guid>> GetRepositoryIdsForProjectAsync(
            Guid tenantId, Guid jiraConnectionId, string projectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byProject.TryGetValue(projectKey, out var ids) ? ids : Array.Empty<Guid>());

        public Task ReplaceAsync(
            Guid tenantId, Guid jiraConnectionId, IReadOnlyList<JiraProjectMappingInput> mappings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRepos : IRepositoryRepository
    {
        private readonly IReadOnlyList<RepositoryLookupRow> _indexed;

        public FakeRepos(IReadOnlyList<RepositoryLookupRow> indexed) => _indexed = indexed;

        public Task<RepositoryLookupRow?> GetByIdAsync(Guid tenantId, Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_indexed.FirstOrDefault(r => r.Id == repositoryId));

        public Task<RepositoryInstallationLookup?> TryResolveByInstallationAndFullNameAsync(
            long installationId, string fullName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetIndexStateAsync(
            Guid tenantId, Guid repositoryId, string commitSha, DateTimeOffset indexedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RepositoryLookupRow>> ListIndexedAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_indexed);
    }

    private sealed class FakeJiraClient : IJiraClient
    {
        private readonly JiraIssueDetails? _issue;

        public FakeJiraClient(JiraIssueDetails? issue) => _issue = issue;

        public string? PostedComment { get; private set; }

        public Task<JiraIssueDetails?> GetIssueAsync(JiraConnectionInfo connection, string issueKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_issue);

        public Task PostCommentAsync(JiraConnectionInfo connection, string issueKey, string body, CancellationToken cancellationToken = default)
        {
            PostedComment = body;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeImprovement : IJiraIssueImprovementService
    {
        public CodeSearchResult? LastCodeContext { get; private set; }

        public Task<(IssueImprovementResult Result, int InputTokens, int OutputTokens, decimal EstimatedCostZar)> ImproveAsync(
            JiraIssueDetails issue,
            CodeSearchResult? codeContext = null,
            Domain.Context.RepoContext? repoContext = null,
            CancellationToken cancellationToken = default)
        {
            LastCodeContext = codeContext;
            return Task.FromResult((
                new IssueImprovementResult("body", Array.Empty<string>(), Array.Empty<string>(), "Low", false),
                1, 1, 0.01m));
        }
    }

    private sealed class ThrowingSearch : ICodeSearchService
    {
        public Task<CodeSearchResult> SearchAsync(
            Guid tenantId, IReadOnlyList<Guid> repositoryIds, string queryText, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("voyage_down");
    }

    private sealed class EmptySearch : ICodeSearchService
    {
        public Task<CodeSearchResult> SearchAsync(
            Guid tenantId, IReadOnlyList<Guid> repositoryIds, string queryText, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeSearchResult.Empty);
    }

    private sealed class FakeContextBuilder : IContextBuilder
    {
        public Task<EngineIQ.Domain.Context.RepoContext?> GetOrBuildAsync(
            Guid tenantId,
            long installationId,
            string owner,
            string repo,
            IReadOnlyList<string> prFilePaths,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EngineIQ.Domain.Context.RepoContext?>(null);
    }
}
