using EngineIQ.ContextBuilder.Search;
using EngineIQ.Domain.Search;

namespace EngineIQ.Tests.Unit;

public class CodeSearchServiceTests
{
    [Fact]
    public void MergeRrf_chunk_in_both_lists_outranks_single_list()
    {
        var bothId = Guid.NewGuid();
        var onlyVector = Guid.NewGuid();
        var onlyText = Guid.NewGuid();

        var vector = new[]
        {
            HitV(bothId, 1),
            HitV(onlyVector, 2),
        };
        var text = new[]
        {
            HitT(bothId, 1),
            HitT(onlyText, 2),
        };

        var merged = CodeSearchService.MergeRrf(vector, text, rrfK: 60);
        Assert.Equal(bothId, ChunkIdFromPath(merged[0])); // highest RRF
        // both: 1/(60+1)+1/(60+1) = 2/61
        // singles: 1/(60+2) = 1/62
        Assert.True(merged[0].Score > merged[1].Score);
        Assert.Equal(2.0 / 61.0, merged[0].Score, 6);
        Assert.Equal(1.0 / 62.0, merged.Single(h => h.FilePath == onlyVector.ToString()).Score, 6);
    }

    [Fact]
    public void ApplyCaps_enforces_per_file_and_keeps_top_three_under_char_budget()
    {
        var repo = Guid.NewGuid();
        var hits = new List<CodeSearchHit>();
        for (var i = 0; i < 10; i++)
        {
            hits.Add(new CodeSearchHit(
                repo, "org/repo", "same/file.cs", null, 1, 10,
                new string('x', 1000),
                Score: 100 - i));
        }

        // Also add unique files after the first three from same file would be capped.
        for (var i = 0; i < 5; i++)
        {
            hits.Add(new CodeSearchHit(
                repo, "org/repo", $"other/{i}.cs", null, 1, 5,
                new string('y', 5000),
                Score: 50 - i));
        }

        var opts = new RetrievalOptions
        {
            MaxHits = 16,
            MaxHitsPerFile = 3,
            MaxContextChars = 12000,
        };

        var capped = CodeSearchService.ApplyCaps(hits, opts);
        Assert.Equal(3, capped.Count(h => h.FilePath == "same/file.cs"));
        Assert.True(capped.Count >= 3);
        Assert.True(capped.Sum(h => h.Content.Length) <= opts.MaxContextChars
                    || capped.Count == 3); // top 3 always kept even if over budget when each is large
    }

    [Fact]
    public void ApplyCaps_always_keeps_at_least_top_three_even_when_over_char_budget()
    {
        var repo = Guid.NewGuid();
        var hits = Enumerable.Range(0, 5)
            .Select(i => new CodeSearchHit(
                repo, "org/repo", $"f{i}.cs", null, 1, 2,
                new string('z', 20000),
                Score: 10 - i))
            .ToList();

        var capped = CodeSearchService.ApplyCaps(hits, new RetrievalOptions
        {
            MaxHits = 16,
            MaxHitsPerFile = 3,
            MaxContextChars = 1000,
        });

        Assert.Equal(3, capped.Count);
    }

    private static VectorHit HitV(Guid id, int rank) =>
        new(id, Guid.NewGuid(), "org/repo", id.ToString(), null, 1, 2, "c", 0.1, rank);

    private static TextHit HitT(Guid id, int rank) =>
        new(id, Guid.NewGuid(), "org/repo", id.ToString(), null, 1, 2, "c", 0.9, rank);

    private static Guid ChunkIdFromPath(CodeSearchHit hit) => Guid.Parse(hit.FilePath);
}
