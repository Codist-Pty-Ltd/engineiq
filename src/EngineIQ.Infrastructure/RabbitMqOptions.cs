namespace EngineIQ.Infrastructure;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";

    public string QueueName { get; set; } = "pr-review-jobs";

    public string DeadLetterQueueName { get; set; } = "pr-review-jobs-dlq";

    public string JiraQueueName { get; set; } = "jira-issue-jobs";

    public string JiraDeadLetterQueueName { get; set; } = "jira-issue-jobs-dlq";

    /// <summary>Repo code-index jobs queue (Session13).</summary>
    public string IndexQueueName { get; set; } = "repo-index-jobs";

    public string IndexDeadLetterQueueName { get; set; } = "repo-index-jobs-dlq";

    /// <summary>Jira backlog backfill jobs queue (Session15).</summary>
    public string BackfillQueueName { get; set; } = "jira-backfill-jobs";

    public string BackfillDeadLetterQueueName { get; set; } = "jira-backfill-jobs-dlq";
}
