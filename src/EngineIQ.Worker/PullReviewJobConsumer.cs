using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngineIQ.AIEngine;
using EngineIQ.Domain.Configuration;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Email;
using EngineIQ.Infrastructure.Telemetry;
using EngineIQ.Observability;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EngineIQ.Worker;

/// <summary>
/// Consumes PR review jobs from RabbitMQ; retries up to 3 failures then dead-letters.
/// </summary>
public sealed class PullReviewJobConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IOptions<RabbitMqOptions> _rabbitOptions;
    private readonly IReviewOrchestrator _orchestrator;
    private readonly IJobRepository _jobRepository;
    private readonly IFindingRepository _findings;
    private readonly ITenantMetricsRepository _tenantMetrics;
    private readonly ITenantRepository _tenants;
    private readonly IEmailNotificationService _email;
    private readonly IOptions<SendGridOptions> _sendGridOptions;
    private readonly IOptions<EngineIQDashboardOptions> _dashboardOptions;
    private readonly ILogger<PullReviewJobConsumer> _logger;

    public PullReviewJobConsumer(
        IOptions<RabbitMqOptions> rabbitOptions,
        IReviewOrchestrator orchestrator,
        IJobRepository jobRepository,
        IFindingRepository findings,
        ITenantMetricsRepository tenantMetrics,
        ITenantRepository tenants,
        IEmailNotificationService email,
        IOptions<SendGridOptions> sendGridOptions,
        IOptions<EngineIQDashboardOptions> dashboardOptions,
        ILogger<PullReviewJobConsumer> logger)
    {
        _rabbitOptions = rabbitOptions;
        _orchestrator = orchestrator;
        _jobRepository = jobRepository;
        _findings = findings;
        _tenantMetrics = tenantMetrics;
        _tenants = tenants;
        _email = email;
        _sendGridOptions = sendGridOptions;
        _dashboardOptions = dashboardOptions;
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

        using var connection = factory.CreateConnection("EngineIQ.Worker");
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
            queue: opts.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueDeclare(
            queue: opts.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, ea) => HandleMessageAsync(channel, opts, ea, stoppingToken);

        channel.BasicConsume(opts.QueueName, autoAck: false, consumer);

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
        PullReviewJobMessage? job = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            job = JsonSerializer.Deserialize<PullReviewJobMessage>(json, JsonOptions);
            if (job is null)
            {
                _logger.LogWarning("Ignoring null job body.");
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }

            var parentContext = TracePropagation.Extract(ea.BasicProperties);
            using var activity = ReviewTelemetry.ActivitySource.StartActivity(
                "review.process",
                ActivityKind.Consumer,
                parentContext);
            using var logScope = ReviewLogScope.Begin(
                _logger,
                job.TenantId,
                job.JobId,
                job.PrNumber,
                job.Owner,
                job.Repo);
            ReviewTelemetry.SetReviewTags(activity, job.TenantId, job.JobId, job.PrNumber);

            await _jobRepository.MarkJobProcessingAsync(job.TenantId, job.JobId, stoppingToken);

            using var reviewCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            reviewCts.CancelAfter(TimeSpan.FromSeconds(90));

            var preferences = await _tenants.GetPortalPreferencesAsync(job.TenantId, reviewCts.Token)
                            ?? new TenantPortalPreferences();
            var configYaml = await _tenants.GetConfigYamlAsync(job.TenantId, reviewCts.Token);

            var sw = Stopwatch.StartNew();
            using var reviewActivity = ReviewTelemetry.StartActivity("review.execute");
            ReviewTelemetry.SetReviewTags(reviewActivity, job.TenantId, job.JobId, job.PrNumber);
            var result = await _orchestrator.ReviewPullRequestAsync(
                new PrReviewJobCommand(
                    job.TenantId,
                    job.InstallationId,
                    job.Owner,
                    job.Repo,
                    job.PrNumber,
                    preferences,
                    configYaml),
                reviewCts.Token);
            sw.Stop();

            if (result.Skipped)
            {
                await _jobRepository.MarkJobSkippedAsync(
                    job.TenantId,
                    job.JobId,
                    result.SkipReason ?? "skipped",
                    stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
                ReviewTelemetry.RecordReviewCompleted("skipped", sw.Elapsed.TotalMilliseconds, 0, 0);
                _logger.LogInformation(
                    "PR review skipped for {Owner}/{Repo}#{Pr}: {Reason}",
                    job.Owner,
                    job.Repo,
                    job.PrNumber,
                    result.SkipReason);
                return;
            }

            var outcome = result.Outcome!;

            await ReviewFindingsPersistence.TryPersistAsync(
                _findings,
                job.TenantId,
                job.JobId,
                outcome.ParsedFindings,
                _logger,
                stoppingToken);

            var metricsDate = DateOnly.FromDateTime(DateTime.UtcNow);
            await ReviewTenantMetricsPersistence.TryRecordJobCompletionAsync(
                _tenantMetrics,
                job.TenantId,
                metricsDate,
                outcome.ParsedFindings.Count,
                sw.ElapsedMilliseconds,
                outcome.EstimatedCostZar,
                _logger,
                stoppingToken);

            await ReviewCriticalIssuesEmailNotifier.TryNotifyAsync(
                _email,
                _tenants,
                _dashboardOptions.Value.DashboardBaseUrl,
                job,
                outcome.ParsedFindings,
                preferences,
                SendGridEmailSettings.IsCriticalIssuesConfigured(_sendGridOptions.Value),
                _logger,
                stoppingToken);

            await _jobRepository.MarkJobCompletedAsync(
                job.TenantId,
                job.JobId,
                sw.ElapsedMilliseconds,
                outcome.FindingsCountEstimate,
                outcome.InputTokens,
                outcome.OutputTokens,
                outcome.EstimatedCostZar,
                stoppingToken);

            channel.BasicAck(ea.DeliveryTag, multiple: false);
            ReviewTelemetry.RecordReviewCompleted(
                "completed",
                sw.Elapsed.TotalMilliseconds,
                outcome.ParsedFindings.Count,
                (double)outcome.EstimatedCostZar);
            ReviewTelemetry.RecordClaudeTokens(outcome.InputTokens, outcome.OutputTokens);
            _logger.LogInformation(
                "PR review completed for {Owner}/{Repo}#{Pr}",
                job.Owner,
                job.Repo,
                job.PrNumber);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PR review job failed for message.");
            ReviewTelemetry.ReviewsTotal.Add(1, new KeyValuePair<string, object?>("status", "failed"));

            if (job is not null && job.Attempt < 3)
            {
                var retry = new PullReviewJobMessage(
                    job.TenantId,
                    job.JobId,
                    job.RepositoryId,
                    job.InstallationId,
                    job.Owner,
                    job.Repo,
                    job.PrNumber,
                    job.Attempt + 1);

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(retry, JsonOptions));
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.QueueName,
                    mandatory: false,
                    basicProperties: props,
                    body: body);

                _logger.LogWarning("Re-queued PR review job (attempt {Attempt}).", retry.Attempt);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            else
            {
                if (job is not null)
                {
                    try
                    {
                        await _jobRepository.MarkJobFailedAsync(job.TenantId, job.JobId, null, stoppingToken);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogWarning(inner, "Could not mark job failed before DLQ.");
                    }
                }

                var dlqProps = channel.CreateBasicProperties();
                dlqProps.Persistent = true;
                dlqProps.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: opts.DeadLetterQueueName,
                    mandatory: false,
                    basicProperties: dlqProps,
                    body: ea.Body);

                _logger.LogError("Sent PR review job to dead-letter queue after max retries.");
                ReviewTelemetry.ReviewsTotal.Add(1, new KeyValuePair<string, object?>("status", "dlq"));
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
        }
    }
}
