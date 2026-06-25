using EngineIQ.Observability;

namespace EngineIQ.Tests.Unit;

public class ReviewTelemetryTests
{
    [Fact]
    public void RecordPersistenceFailure_does_not_throw()
    {
        var ex = Record.Exception(() => ReviewTelemetry.RecordPersistenceFailure("findings"));
        Assert.Null(ex);
    }

    [Fact]
    public void RecordReviewCompleted_records_histograms_without_throw()
    {
        var ex = Record.Exception(() =>
            ReviewTelemetry.RecordReviewCompleted("completed", 1200, 3, 0.42));
        Assert.Null(ex);
    }

    [Fact]
    public void SetQueueDepth_accepts_values_without_throw()
    {
        var ex = Record.Exception(() => ReviewTelemetry.SetQueueDepth(7));
        Assert.Null(ex);
    }
}
