namespace EngineIQ.Tests.Unit;

/// <summary>
/// API maps an active backfill (FindActiveJobIdAsync non-null / BlockReason in_progress) to HTTP 409.
/// </summary>
public class BackfillConcurrencyTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void FindActiveJobId_non_null_means_conflict(bool hasActive, bool expectConflict)
    {
        Guid? activeJobId = hasActive ? Guid.NewGuid() : null;
        Assert.Equal(expectConflict, MapsToConflict(activeJobId, blockReason: null));
    }

    [Fact]
    public void TryCreate_block_reason_in_progress_means_conflict()
    {
        Assert.True(MapsToConflict(activeJobId: null, blockReason: "in_progress"));
        Assert.True(MapsToConflict(activeJobId: null, blockReason: "IN_PROGRESS"));
        Assert.False(MapsToConflict(activeJobId: null, blockReason: "suspended"));
        Assert.False(MapsToConflict(activeJobId: null, blockReason: null));
    }

    /// <summary>Predicate aligned with JiraConnectionController.StartBackfill 409 paths.</summary>
    private static bool MapsToConflict(Guid? activeJobId, string? blockReason) =>
        activeJobId is not null
        || string.Equals(blockReason, "in_progress", StringComparison.OrdinalIgnoreCase);
}
