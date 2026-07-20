using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Telemetry;
using EngineIQ.Observability;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EngineIQ.Worker;

/// <summary>
/// Consumes repository code-index jobs from RabbitMQ; one job at a time per worker (indexing is I/O and
/// CPU heavy), 10 minute hard timeout per job, retries up to 3 failures then dead-letters.
/// </summary>
public sealed class RepoIndexJobConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOptions<RabbitMqOptions> _rabbitOptions;
    private readonly IRepoIndexer _indexer;
    private readonly IRepoIndexJobRepository _jobs;
    private readonly ITenantMetricsRepository _tenantMetrics;
    private readonly ILogger<RepoIndexJobConsumer> _logger;

    public RepoIndexJobConsumer(
        IOptions<RabbitMqOptions> rabbitOptions,
        IRepoIndexer indexer,
        IRepoIndexJobRepository jobs,
        ITenantMetricsRepository tenantMetrics,
        ILogger<RepoIndexJobConsumer> logger)
    {
        _rabbitOptions = rabbitOptions;
        _indexer = indexer;
        _jobs = jobs;
        _tenantMetrics = tenantMetrics;
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

        using var connection = factory.CreateConnection("EngineIQ.Worker.RepoIndex");
        using var channel = connection.CreateModel();
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        channel.QueueDeclare(
            queue: opts.IndexQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueDeclare(
            queue: opts.IndexDeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleMessageAsync(channel, opts, ea, stoppingToken);

        channel.BasicConsume(opts.IndexQueueName, autoAck: false, consumer);

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
        RepoIndexJobMessage? job = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            job = JsonSerializer.Deserialize<RepoIndexJobMessage>(json, JsonOptions);
            if (job is null)
            {
                _logger.LogWarning("Ignoring null repo index job body.");
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            var parentContext = TracePropagation.Extract(ea.BasicProperties);
            using var activity = ReviewTelemetry.ActivitySource.StartActivity(
                "repo.index.process",
                ActivityKind.Consumer,
                parentContext);
            activity?.SetTag("tenant.id", job.TenantId);
            activity?.SetTag("job.id", job.JobId);
            activity?.SetTag("repo.full_name", $"{job.Owner}/{job.Repo}");

            if (!await _jobs.TryMarkJobProcessingIfQueuedAsync(job.TenantId, job.JobId, stoppingToken))
            {
                _logger.LogInformation(
                    "Skipping duplicate or stale repo index queue message for job {JobId} (not Queued).",
                    job.JobId);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            using var indexCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            indexCts.CancelAfter(TimeSpan.FromMinutes(10));

            var sw = Stopwatch.StartNew();
            using var indexActivity = ReviewTelemetry.StartActivity("repo.index.execute");
            var stats = await _indexer.IndexAsync(job, indexCts.Token);
            sw.Stop();

            await _jobs.MarkJobCompletedAsync(
                job.TenantId,
                job.JobId,
                sw.ElapsedMilliseconds,
                stats.FilesWalked,
                stats.ChunksTotal,
                stats.ChunksEmbedded,
                stats.ChunksDeleted,
                stoppingToken);

            if (stats.ChunksEmbedded > 0)
            {
                try
                {
                    await _tenantMetrics.RecordChunksEmbeddedAsync(
                        job.TenantId,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        stats.ChunksEmbedded,
                        stoppingToken);
                }
                catch (Exception metricsEx)
                {
                    _logger.LogWarning(metricsEx, "Could not record code-index metrics for job {JobId}.", job.JobId);
                }
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
            _logger.LogInformation(
                "Repo index job completed for {Owner}/{Repo}: FilesWalked={FilesWalked} ChunksTotal={ChunksTotal} ChunksEmbedded={ChunksEmbedded} ChunksDeleted={ChunksDeleted} in {Ms} ms.",
                job.Owner,
                job.Repo,
                stats.FilesWalked,
                stats.ChunksTotal,
                stats.ChunksEmbedded,
                stats.ChunksDeleted,
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repo index job failed for message.");

            if (job is not null && job.Attempt < 3)
            {
                var retry = new RepoIndexJobMessage(
                    job.TenantId,
                    job.JobId,
                    job.RepositoryId,
                    job.InstallationId,
                    job.Owner,
                    job.Repo,
                    job.HeadSha,
                    job.BaseSha,
                    job.Attempt + 1);

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retry, JsonOptions));
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.IndexQueueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body);

                _logger.LogWarning("Re-queued repo index job (attempt {Attempt}).", retry.Attempt);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            else
            {
                if (job is not null)
                {
                    try
                    {
                        await _jobs.MarkJobFailedAsync(
                            job.TenantId,
                            job.JobId,
                            failureReason: ex.Message,
                            durationMs: null,
                            stoppingToken);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogWarning(inner, "Could not mark repo index job failed before DLQ.");
                    }
                }

                var dlqProps = channel.CreateBasicProperties();
                dlqProps.Persistent = true;
                dlqProps.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.IndexDeadLetterQueueName,
                    mandatory: false,
                    basicProperties: dlqProps,
                    body: ea.Body);

                _logger.LogError("Sent repo index job to dead-letter queue after max retries.");
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
        }
    }
}
