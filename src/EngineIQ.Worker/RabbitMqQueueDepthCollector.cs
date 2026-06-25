using EngineIQ.Infrastructure;
using EngineIQ.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EngineIQ.Worker;

/// <summary>Periodically samples RabbitMQ queue depth for Prometheus gauges.</summary>
public sealed class RabbitMqQueueDepthCollector : BackgroundService
{
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<RabbitMqQueueDepthCollector> _logger;

    public RabbitMqQueueDepthCollector(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqQueueDepthCollector> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var depth = SampleQueueDepth();
                ReviewTelemetry.SetQueueDepth(depth);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Queue depth sample failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private int SampleQueueDepth()
    {
        var opts = _options.Value;
        var factory = new ConnectionFactory
        {
            Uri = new Uri(opts.ConnectionString),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        };

        using var connection = factory.CreateConnection("EngineIQ.Worker.QueueDepth");
        using var channel = connection.CreateModel();
        var declare = channel.QueueDeclarePassive(opts.QueueName);
        return (int)declare.MessageCount;
    }
}
