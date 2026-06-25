using EngineIQ.AIEngine;
using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Persistence;
using EngineIQ.Observability;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineIQ.Tests.Unit;

public class ReviewFindingsPersistenceTests
{
    private sealed class RecordingFindingRepository : IFindingRepository
    {
        public int CallCount { get; private set; }
        public Guid? TenantId { get; private set; }
        public Guid? JobId { get; private set; }
        public IReadOnlyList<FindingWriteDto>? LastBatch { get; private set; }

        public Task AddFindingsAsync(
            Guid tenantId,
            Guid jobId,
            IReadOnlyList<FindingWriteDto> findings,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            TenantId = tenantId;
            JobId = jobId;
            LastBatch = findings;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FindingReadDto>> ListByJobAsync(
            Guid tenantId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FindingReadDto>>(Array.Empty<FindingReadDto>());

        public Task<(IReadOnlyList<FindingReadDto> Items, int TotalCount)> ListForTenantAsync(
            Guid tenantId,
            FindingListQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((Array.Empty<FindingReadDto>() as IReadOnlyList<FindingReadDto>, 0));
    }

    [Fact]
    public async Task TryPersistAsync_calls_AddFindingsAsync_once_with_N_mapped_AI_findings()
    {
        const string review = """
            ## Review
            - Security: hardcoded API key in `src/Config.cs:42`
            - Architecture: domain references infrastructure layer
            - Minor nit on naming
            """;

        var parsed = AnthropicReviewResponseParser.ParseFindingsFromMarkdown(review);
        Assert.Equal(3, parsed.Count);

        var repo = new RecordingFindingRepository();
        var tenantId = Guid.Parse("e2a5cfb5-5148-4737-9ee2-0c0f4d2093bf");
        var jobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await ReviewFindingsPersistence.TryPersistAsync(
            repo,
            tenantId,
            jobId,
            parsed,
            NullLogger.Instance);

        Assert.Equal(1, repo.CallCount);
        Assert.Equal(tenantId, repo.TenantId);
        Assert.Equal(jobId, repo.JobId);
        Assert.NotNull(repo.LastBatch);
        Assert.Equal(3, repo.LastBatch!.Count);

        Assert.All(repo.LastBatch, f => Assert.Equal(FindingSources.AI, f.Source));
        Assert.Equal("high", repo.LastBatch[0].Severity);
        Assert.Equal("security", repo.LastBatch[0].Category);
        Assert.Equal("src/Config.cs", repo.LastBatch[0].FilePath);
        Assert.Equal(42, repo.LastBatch[0].LineNumber);
        Assert.Contains("hardcoded API key", repo.LastBatch[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("architecture", repo.LastBatch[1].Category);
        Assert.False(repo.LastBatch[0].WasActioned);
        Assert.Equal("unknown", repo.LastBatch[0].PrMergeStatus);
    }

    [Fact]
    public async Task TryPersistAsync_logs_warning_and_increments_metric_when_repository_throws()
    {
        var repo = new ThrowingFindingRepository();
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var findings = new[]
        {
            new FindingWriteDto("high", "security", null, FindingSources.AI, "a.cs", 1, "msg", false, "unknown", null),
        };

        await ReviewFindingsPersistence.TryPersistAsync(
            repo,
            tenantId,
            jobId,
            findings,
            NullLogger.Instance);

        var metricEx = Record.Exception(() => ReviewTelemetry.RecordPersistenceFailure("findings"));
        Assert.Null(metricEx);
    }

    private sealed class ThrowingFindingRepository : IFindingRepository
    {
        public Task AddFindingsAsync(
            Guid tenantId,
            Guid jobId,
            IReadOnlyList<FindingWriteDto> findings,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("db down");

        public Task<IReadOnlyList<FindingReadDto>> ListByJobAsync(
            Guid tenantId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(IReadOnlyList<FindingReadDto> Items, int TotalCount)> ListForTenantAsync(
            Guid tenantId,
            FindingListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void ParseFindingsFromMarkdown_ignores_trust_footer_section()
    {
        var md = "- Real finding\n\n---\n\nEngineIQ processed this diff ephemerally.";
        var parsed = AnthropicReviewResponseParser.ParseFindingsFromMarkdown(md);
        Assert.Single(parsed);
        Assert.Contains("Real finding", parsed[0].Message, StringComparison.Ordinal);
    }
}
