using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EngineIQ.Observability;

/// <summary>OpenTelemetry traces and metrics for the PR review pipeline.</summary>
public static class ReviewTelemetry
{
    public const string ActivitySourceName = "EngineIQ.Review";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public const string MeterName = "EngineIQ.Review";
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ReviewsTotal =
        Meter.CreateCounter<long>(
            "engineiq.reviews.total",
            description: "PR review jobs by terminal status");

    public static readonly Histogram<double> ReviewDurationMs =
        Meter.CreateHistogram<double>(
            "engineiq.review.duration_ms",
            unit: "ms",
            description: "End-to-end worker review duration");

    public static readonly Histogram<int> ReviewFindingsCount =
        Meter.CreateHistogram<int>(
            "engineiq.review.findings_count",
            description: "Merged findings persisted per completed review");

    public static readonly Histogram<double> ClaudeCostZar =
        Meter.CreateHistogram<double>(
            "engineiq.claude.cost_zar",
            unit: "ZAR",
            description: "Estimated Claude cost per review");

    public static readonly Counter<long> ClaudeTokensTotal =
        Meter.CreateCounter<long>(
            "engineiq.claude.tokens.total",
            description: "Claude tokens consumed");

    public static readonly Counter<long> WebhookEnqueueTotal =
        Meter.CreateCounter<long>(
            "engineiq.webhook.enqueue.total",
            description: "GitHub webhook enqueue outcomes");

    public static readonly Histogram<double> WebhookEnqueueDurationMs =
        Meter.CreateHistogram<double>(
            "engineiq.webhook.enqueue.duration_ms",
            unit: "ms",
            description: "Webhook handler duration through RabbitMQ publish");

    public static readonly Counter<long> PersistenceFailuresTotal =
        Meter.CreateCounter<long>(
            "engineiq.persistence.failures.total",
            description: "Best-effort persistence paths that logged warnings");

    public static readonly ObservableGauge<int> QueueDepth =
        Meter.CreateObservableGauge(
            "engineiq.queue.depth",
            () => QueueDepthMeasurement,
            description: "RabbitMQ ready messages on the PR review queue");

    private static int _queueDepth;

    public static void SetQueueDepth(int depth) => Interlocked.Exchange(ref _queueDepth, depth);

    private static Measurement<int> QueueDepthMeasurement =>
        new(Interlocked.CompareExchange(ref _queueDepth, 0, 0));

    public static void RecordReviewCompleted(string status, double durationMs, int findingsCount, double costZar)
    {
        ReviewsTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        ReviewDurationMs.Record(durationMs);
        ReviewFindingsCount.Record(findingsCount);
        ClaudeCostZar.Record(costZar);
    }

    public static void RecordClaudeTokens(long inputTokens, long outputTokens)
    {
        if (inputTokens > 0)
            ClaudeTokensTotal.Add(inputTokens, new KeyValuePair<string, object?>("direction", "input"));
        if (outputTokens > 0)
            ClaudeTokensTotal.Add(outputTokens, new KeyValuePair<string, object?>("direction", "output"));
    }

    public static void RecordPersistenceFailure(string type) =>
        PersistenceFailuresTotal.Add(1, new KeyValuePair<string, object?>("type", type));

    public static void RecordWebhookEnqueue(string result, double durationMs)
    {
        WebhookEnqueueTotal.Add(1, new KeyValuePair<string, object?>("result", result));
        WebhookEnqueueDurationMs.Record(durationMs);
    }

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(name, kind);

    public static void SetReviewTags(Activity? activity, Guid tenantId, Guid jobId, int prNumber)
    {
        if (activity is null)
            return;

        activity.SetTag("tenant_id", tenantId);
        activity.SetTag("job_id", jobId);
        activity.SetTag("pr_number", prNumber);
    }
}
