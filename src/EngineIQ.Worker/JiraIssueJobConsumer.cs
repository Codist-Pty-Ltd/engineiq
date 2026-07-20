using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Infrastructure.Telemetry;
using EngineIQ.Observability;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EngineIQ.Worker;

/// <summary>
/// Consumes Jira issue analysis jobs from RabbitMQ; retries up to 3 failures then dead-letters.
/// <see cref="IssueNotFoundException"/> is non-retryable.
/// </summary>
public sealed class JiraIssueJobConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOptions<RabbitMqOptions> _rabbitOptions;
    private readonly IIssueAnalysisOrchestrator _orchestrator;
    private readonly IIssueAnalysisJobRepository _jobs;
    private readonly IJiraConnectionRepository _connections;
    private readonly IJiraApiTokenProtector _tokenProtector;
    private readonly ITenantMetricsRepository _tenantMetrics;
    private readonly ILogger<JiraIssueJobConsumer> _logger;

    public JiraIssueJobConsumer(
        IOptions<RabbitMqOptions> rabbitOptions,
        IIssueAnalysisOrchestrator orchestrator,
        IIssueAnalysisJobRepository jobs,
        IJiraConnectionRepository connections,
        IJiraApiTokenProtector tokenProtector,
        ITenantMetricsRepository tenantMetrics,
        ILogger<JiraIssueJobConsumer> logger)
    {
        _rabbitOptions = rabbitOptions;
        _orchestrator = orchestrator;
        _jobs = jobs;
        _connections = connections;
        _tokenProtector = tokenProtector;
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

        using var connection = factory.CreateConnection("EngineIQ.Worker.Jira");
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
            queue: opts.JiraQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueDeclare(
            queue: opts.JiraDeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleMessageAsync(channel, opts, ea, stoppingToken);

        channel.BasicConsume(opts.JiraQueueName, autoAck: false, consumer);

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
        JiraIssueAnalysisJobMessage? job = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            job = JsonSerializer.Deserialize<JiraIssueAnalysisJobMessage>(json, JsonOptions);
            if (job is null)
            {
                _logger.LogWarning("Ignoring null Jira job body.");
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            var parentContext = TracePropagation.Extract(ea.BasicProperties);
            using var activity = ReviewTelemetry.ActivitySource.StartActivity(
                "jira.issue.process",
                ActivityKind.Consumer,
                parentContext);
            activity?.SetTag("tenant.id", job.TenantId);
            activity?.SetTag("job.id", job.JobId);
            activity?.SetTag("jira.issue_key", job.IssueKey);

            if (!await _jobs.TryMarkJobProcessingIfQueuedAsync(job.TenantId, job.JobId, stoppingToken))
            {
                _logger.LogInformation(
                    "Skipping duplicate or stale Jira queue message for job {JobId} (not Queued).",
                    job.JobId);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            var connectionRow = await _connections.GetByIdAsync(
                job.TenantId,
                job.JiraConnectionId,
                stoppingToken);
            if (connectionRow is null || !connectionRow.Enabled)
            {
                await _jobs.MarkJobFailedAsync(
                    job.TenantId,
                    job.JobId,
                    "connection_unavailable",
                    durationMs: null,
                    stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                _logger.LogWarning(
                    "Jira connection {ConnectionId} unavailable for job {JobId}.",
                    job.JiraConnectionId,
                    job.JobId);
                return;
            }

            string apiToken;
            try
            {
                apiToken = _tokenProtector.Unprotect(connectionRow.ApiTokenProtected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt Jira API token for connection {ConnectionId}.", job.JiraConnectionId);
                await _jobs.MarkJobFailedAsync(
                    job.TenantId,
                    job.JobId,
                    "token_unprotect_failed",
                    durationMs: null,
                    stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            using var analysisCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            analysisCts.CancelAfter(TimeSpan.FromSeconds(90));

            var command = new JiraIssueAnalysisJobCommand(
                job.TenantId,
                job.JobId,
                job.JiraConnectionId,
                job.IssueKey,
                job.JiraIssueId,
                new JiraConnectionInfo(connectionRow.SiteBaseUrl, connectionRow.Email, apiToken));

            var sw = Stopwatch.StartNew();
            using var analysisActivity = ReviewTelemetry.StartActivity("jira.issue.execute");
            var outcome = await _orchestrator.AnalyzeIssueAsync(command, analysisCts.Token);
            sw.Stop();

            await _jobs.MarkJobCompletedAsync(
                job.TenantId,
                job.JobId,
                sw.ElapsedMilliseconds,
                outcome.InputTokens,
                outcome.OutputTokens,
                outcome.EstimatedCostZar,
                stoppingToken,
                outcome.ReposSearched,
                outcome.ChunksRetrieved);

            try
            {
                await _tenantMetrics.RecordIssueAnalysisCompletionAsync(
                    job.TenantId,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    sw.ElapsedMilliseconds,
                    outcome.EstimatedCostZar,
                    stoppingToken);
            }
            catch (Exception metricsEx)
            {
                _logger.LogWarning(metricsEx, "Could not record issue analysis metrics for job {JobId}.", job.JobId);
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
            ReviewTelemetry.RecordReviewCompleted(
                "completed",
                sw.Elapsed.TotalMilliseconds,
                findingsCount: 0,
                (double)outcome.EstimatedCostZar);
            ReviewTelemetry.RecordClaudeTokens(outcome.InputTokens, outcome.OutputTokens);
            _logger.LogInformation("Jira issue analysis completed for {IssueKey}", job.IssueKey);
        }
        catch (IssueNotFoundException ex)
        {
            if (job is not null)
            {
                try
                {
                    await _jobs.MarkJobFailedAsync(
                        job.TenantId,
                        job.JobId,
                        $"issue_not_found:{ex.IssueKey}",
                        durationMs: null,
                        stoppingToken);
                }
                catch (Exception inner)
                {
                    _logger.LogWarning(inner, "Could not mark Jira job failed after issue not found.");
                }
            }

            _logger.LogWarning("Jira issue not found for key {IssueKey}; failing without retry.", ex.IssueKey);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jira issue analysis job failed for message.");
            ReviewTelemetry.ReviewsTotal.Add(1, new KeyValuePair<string, object?>("status", "failed"));

            if (job is not null && job.Attempt < 3)
            {
                var retry = new JiraIssueAnalysisJobMessage(
                    job.TenantId,
                    job.JobId,
                    job.JiraConnectionId,
                    job.IssueKey,
                    job.JiraIssueId,
                    job.Attempt + 1);

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retry, JsonOptions));
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.JiraQueueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body);

                _logger.LogWarning("Re-queued Jira issue analysis job (attempt {Attempt}).", retry.Attempt);
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
                            failureReason: null,
                            durationMs: null,
                            stoppingToken);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogWarning(inner, "Could not mark Jira job failed before DLQ.");
                    }
                }

                var dlqProps = channel.CreateBasicProperties();
                dlqProps.Persistent = true;
                dlqProps.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.JiraDeadLetterQueueName,
                    mandatory: false,
                    basicProperties: dlqProps,
                    body: ea.Body);

                _logger.LogError("Sent Jira issue analysis job to dead-letter queue after max retries.");
                ReviewTelemetry.ReviewsTotal.Add(1, new KeyValuePair<string, object?>("status", "dlq"));
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
        }
    }
}
