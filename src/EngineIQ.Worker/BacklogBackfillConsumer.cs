using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Jira;
using EngineIQ.Observability;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EngineIQ.Worker;

/// <summary>
/// Long-running paced backlog backfill: pages Jira search and enqueues issue analysis jobs.
/// </summary>
public sealed class BacklogBackfillConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public const int PageSize = 50;

    private readonly IOptions<RabbitMqOptions> _rabbitOptions;
    private readonly IOptions<JiraClientOptions> _jiraOptions;
    private readonly IBacklogBackfillRepository _backfills;
    private readonly IJiraConnectionRepository _connections;
    private readonly IJiraApiTokenProtector _tokenProtector;
    private readonly IJiraClient _jiraClient;
    private readonly IAnalyzedIssueRepository _analyzedIssues;
    private readonly IIssueAnalysisJobRepository _issueJobs;
    private readonly IJiraIssueAnalysisJobPublisher _issuePublisher;
    private readonly IBackfillPacer _pacer;
    private readonly ILogger<BacklogBackfillConsumer> _logger;

    public BacklogBackfillConsumer(
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<JiraClientOptions> jiraOptions,
        IBacklogBackfillRepository backfills,
        IJiraConnectionRepository connections,
        IJiraApiTokenProtector tokenProtector,
        IJiraClient jiraClient,
        IAnalyzedIssueRepository analyzedIssues,
        IIssueAnalysisJobRepository issueJobs,
        IJiraIssueAnalysisJobPublisher issuePublisher,
        IBackfillPacer pacer,
        ILogger<BacklogBackfillConsumer> logger)
    {
        _rabbitOptions = rabbitOptions;
        _jiraOptions = jiraOptions;
        _backfills = backfills;
        _connections = connections;
        _tokenProtector = tokenProtector;
        _jiraClient = jiraClient;
        _analyzedIssues = analyzedIssues;
        _issueJobs = issueJobs;
        _issuePublisher = issuePublisher;
        _pacer = pacer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _rabbitOptions.Value;
        var factory = new ConnectionFactory
        {
            Uri = new Uri(opts.ConnectionString),
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        using var connection = factory.CreateConnection("EngineIQ.Worker.Backfill");
        using var channel = connection.CreateModel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        channel.QueueDeclare(opts.BackfillQueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(opts.BackfillDeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleMessageAsync(channel, opts, ea, stoppingToken);
        channel.BasicConsume(opts.BackfillQueueName, autoAck: false, consumer);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private async Task HandleMessageAsync(
        IModel channel,
        RabbitMqOptions opts,
        BasicDeliverEventArgs ea,
        CancellationToken stoppingToken)
    {
        BacklogBackfillJobMessage? job = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            job = JsonSerializer.Deserialize<BacklogBackfillJobMessage>(json, JsonOptions);
            if (job is null)
            {
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            if (!await _backfills.TryMarkProcessingIfQueuedAsync(job.TenantId, job.JobId, stoppingToken))
            {
                _logger.LogInformation("Skipping stale/duplicate backfill job {JobId}.", job.JobId);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromMinutes(Math.Max(5, _jiraOptions.Value.BackfillTimeoutMinutes)));

            await RunBackfillAsync(job, cts.Token);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (InvalidJqlException ex)
        {
            _logger.LogWarning(ex, "Backfill failed with invalid JQL.");
            if (job is not null)
                await _backfills.MarkFailedAsync(job.TenantId, job.JobId, ex.Message, stoppingToken);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backfill job failed.");
            if (job is not null && job.Attempt < 3)
            {
                var retry = job with { Attempt = job.Attempt + 1 };
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retry, JsonOptions));
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                channel.BasicPublish(string.Empty, opts.BackfillQueueName, false, props, body);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            else
            {
                if (job is not null)
                    await _backfills.MarkFailedAsync(job.TenantId, job.JobId, ex.Message, stoppingToken);
                channel.BasicPublish(string.Empty, opts.BackfillDeadLetterQueueName, false, null, ea.Body);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
        }
    }

    public async Task RunBackfillAsync(BacklogBackfillJobMessage job, CancellationToken cancellationToken)
    {
        var row = await _backfills.GetByIdAsync(job.TenantId, job.JobId, cancellationToken)
                  ?? throw new InvalidOperationException("backfill_not_found");

        var connectionRow = await _connections.GetByIdAsync(job.TenantId, job.JiraConnectionId, cancellationToken)
                            ?? throw new InvalidOperationException("connection_unavailable");
        var apiToken = _tokenProtector.Unprotect(connectionRow.ApiTokenProtected);
        var connection = new JiraConnectionInfo(connectionRow.SiteBaseUrl, connectionRow.Email, apiToken);

        var cursor = row.StartAtCursor;
        var enqueued = row.EnqueuedCount;
        var skipped = row.SkippedCount;
        var matchedTotal = row.MatchedTotal;
        var maxIssues = row.MaxIssues;
        var delayMs = Math.Max(0, _jiraOptions.Value.BackfillDelayMs);
        var firstPublish = true;

        while (enqueued < maxIssues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _jiraClient.SearchIssuesAsync(connection, row.Jql, cursor, PageSize, cancellationToken);
            matchedTotal = Math.Max(matchedTotal, page.Total);

            if (page.Issues.Count == 0)
                break;

            foreach (var issue in page.Issues)
            {
                if (enqueued >= maxIssues)
                    break;

                var analyzed = await _analyzedIssues.GetByIssueAsync(
                    job.TenantId, job.JiraConnectionId, issue.Id, cancellationToken);
                if (analyzed is not null && analyzed.LastAnalyzedIssueUpdatedAt >= issue.UpdatedAt)
                {
                    skipped++;
                    continue;
                }

                var dedupeKey = $"{issue.Id}:{issue.UpdatedAt:O}";
                var enqueue = await _issueJobs.TryCreateQueuedJobAsync(
                    job.TenantId,
                    job.JiraConnectionId,
                    issue.Key,
                    issue.Id,
                    dedupeKey,
                    cancellationToken,
                    AnalysisTrigger.Backfill);

                if (!enqueue.Created && !enqueue.NeedsRepublish)
                {
                    skipped++;
                    continue;
                }

                if (!firstPublish && delayMs > 0)
                    await _pacer.DelayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
                firstPublish = false;

                var message = new JiraIssueAnalysisJobMessage(
                    job.TenantId,
                    enqueue.JobId!.Value,
                    job.JiraConnectionId,
                    issue.Key,
                    issue.Id,
                    Attempt: 0,
                    Trigger: AnalysisTrigger.Backfill);

                try
                {
                    await _issuePublisher.PublishAsync(message, cancellationToken);
                    await _issueJobs.MarkJobQueuedAfterPublishAsync(job.TenantId, enqueue.JobId.Value, cancellationToken);
                    enqueued++;
                }
                catch
                {
                    // leave PendingPublish for relay
                    enqueued++;
                }
            }

            cursor = page.StartAt + page.Issues.Count;
            await _backfills.UpdateProgressAsync(
                job.TenantId, job.JobId, cursor, matchedTotal, enqueued, skipped, cancellationToken);

            if (page.StartAt + page.Issues.Count >= page.Total)
                break;
        }

        await _backfills.MarkCompletedAsync(job.TenantId, job.JobId, matchedTotal, enqueued, skipped, cancellationToken);
        _logger.LogInformation(
            "Backfill {JobId} completed Matched={Matched} Enqueued={Enqueued} Skipped={Skipped}.",
            job.JobId, matchedTotal, enqueued, skipped);
    }
}

/// <summary>Injectable delay for backfill pacing (tests can use a no-op / counting pacer).</summary>
public interface IBackfillPacer
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class TaskBackfillPacer : IBackfillPacer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
