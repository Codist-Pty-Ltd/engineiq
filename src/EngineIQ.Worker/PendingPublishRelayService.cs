using EngineIQ.Domain.Configuration;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Worker;

/// <summary>
/// Republishes PR review and Jira issue analysis jobs stuck in PendingPublish when the webhook could not reach RabbitMQ.
/// GitHub/Jira redelivery is a secondary recovery path; this reconciler closes the gap without deleting rows.
/// </summary>
public sealed class PendingPublishRelayService : BackgroundService
{
    private readonly IJobRepository _jobs;
    private readonly IPullReviewJobPublisher _publisher;
    private readonly IIssueAnalysisJobRepository _jiraJobs;
    private readonly IJiraIssueAnalysisJobPublisher _jiraPublisher;
    private readonly IOptions<PendingPublishRelayOptions> _options;
    private readonly ILogger<PendingPublishRelayService> _logger;

    public PendingPublishRelayService(
        IJobRepository jobs,
        IPullReviewJobPublisher publisher,
        IIssueAnalysisJobRepository jiraJobs,
        IJiraIssueAnalysisJobPublisher jiraPublisher,
        IOptions<PendingPublishRelayOptions> options,
        ILogger<PendingPublishRelayService> logger)
    {
        _jobs = jobs;
        _publisher = publisher;
        _jiraJobs = jiraJobs;
        _jiraPublisher = jiraPublisher;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, opts.PollIntervalSeconds));
        var staleAfter = TimeSpan.FromSeconds(Math.Max(5, opts.StaleAfterSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayStaleJobsAsync(staleAfter, stoppingToken);
                await RelayStaleJiraJobsAsync(staleAfter, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingPublish reconciler iteration failed.");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RelayStaleJobsAsync(TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        var pending = await _jobs.ListStalePendingPublishJobsAsync(staleAfter, limit: 50, cancellationToken);
        if (pending.Count == 0)
            return;

        _logger.LogInformation("Reconciling {Count} stale PendingPublish job(s).", pending.Count);

        foreach (var job in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var message = new PullReviewJobMessage(
                job.TenantId,
                job.JobId,
                job.RepositoryId,
                job.InstallationId,
                job.Owner,
                job.Repo,
                job.PrNumber,
                Attempt: 0);

            try
            {
                await _publisher.PublishAsync(message, cancellationToken);
                var marked = await _jobs.MarkJobQueuedAfterPublishAsync(job.TenantId, job.JobId, cancellationToken);
                if (marked)
                {
                    _logger.LogInformation(
                        "Reconciler published job {JobId} for tenant {TenantId}.",
                        job.JobId,
                        job.TenantId);
                }
                else
                {
                    _logger.LogDebug(
                        "Job {JobId} was no longer PendingPublish after reconciler publish (likely webhook race).",
                        job.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Reconciler could not publish job {JobId}; will retry on next poll or GitHub redelivery.",
                    job.JobId);
            }
        }
    }

    private async Task RelayStaleJiraJobsAsync(TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        var pending = await _jiraJobs.ListStalePendingPublishJobsAsync(staleAfter, limit: 50, cancellationToken);
        if (pending.Count == 0)
            return;

        _logger.LogInformation("Reconciling {Count} stale PendingPublish Jira job(s).", pending.Count);

        foreach (var job in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var message = new JiraIssueAnalysisJobMessage(
                job.TenantId,
                job.JobId,
                job.JiraConnectionId,
                job.IssueKey,
                job.JiraIssueId,
                Attempt: 0);

            try
            {
                await _jiraPublisher.PublishAsync(message, cancellationToken);
                var marked = await _jiraJobs.MarkJobQueuedAfterPublishAsync(job.TenantId, job.JobId, cancellationToken);
                if (marked)
                {
                    _logger.LogInformation(
                        "Reconciler published Jira job {JobId} for tenant {TenantId}.",
                        job.JobId,
                        job.TenantId);
                }
                else
                {
                    _logger.LogDebug(
                        "Jira job {JobId} was no longer PendingPublish after reconciler publish (likely webhook race).",
                        job.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Reconciler could not publish Jira job {JobId}; will retry on next poll or Jira redelivery.",
                    job.JobId);
            }
        }
    }
}

public sealed class PendingPublishRelayOptions
{
    public const string SectionName = "PendingPublishRelay";

    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>Avoid racing the in-flight webhook handler (450 ms budget).</summary>
    public int StaleAfterSeconds { get; set; } = 30;
}
