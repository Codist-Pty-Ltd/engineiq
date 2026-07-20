using EngineIQ.API.Indexing;

namespace EngineIQ.Tests.Unit;

public class RepoIndexPushFilterTests
{
    [Theory]
    [InlineData("refs/heads/main", "main", true)]
    [InlineData("refs/heads/master", "master", true)]
    [InlineData("refs/heads/feature/x", "main", false)]
    [InlineData("refs/tags/v1", "main", false)]
    public void IsDefaultBranchPush_filters_feature_branches(string gitRef, string defaultBranch, bool expected) =>
        Assert.Equal(expected, RepoIndexPushFilter.IsDefaultBranchPush(gitRef, defaultBranch));

    [Fact]
    public void CanEnqueueIncremental_false_when_never_indexed()
    {
        Assert.False(RepoIndexPushFilter.CanEnqueueIncremental(null));
        Assert.False(RepoIndexPushFilter.CanEnqueueIncremental(""));
        Assert.True(RepoIndexPushFilter.CanEnqueueIncremental("abc123"));
    }

    [Fact]
    public void BuildPushDedupeKey_uses_repository_and_after_sha()
    {
        var repoId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:deadbeef", RepoIndexPushFilter.BuildPushDedupeKey(repoId, "deadbeef"));
    }
}
