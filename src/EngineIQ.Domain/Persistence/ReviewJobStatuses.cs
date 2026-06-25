namespace EngineIQ.Domain.Persistence;

public static class ReviewJobStatuses
{
    /// <summary>Job row committed; RabbitMQ publish not yet confirmed.</summary>
    public const string PendingPublish = "PendingPublish";

    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
}
