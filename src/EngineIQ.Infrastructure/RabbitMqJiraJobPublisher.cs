using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Messaging;
using EngineIQ.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EngineIQ.Infrastructure;

public sealed class RabbitMqJiraJobPublisher : IJiraIssueAnalysisJobPublisher, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqJiraJobPublisher> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public RabbitMqJiraJobPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqJiraJobPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(JiraIssueAnalysisJobMessage job, CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            _connection ??= CreateConnection();
            using var channel = _connection.CreateModel();
            channel.QueueDeclare(
                queue: _options.JiraQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job, SerializerOptions));
            var props = channel.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            TracePropagation.Inject(props);

            channel.BasicPublish(
                exchange: string.Empty,
                routingKey: _options.JiraQueueName,
                mandatory: false,
                basicProperties: props,
                body: body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish Jira issue analysis job to RabbitMQ.");
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString),
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };
        return factory.CreateConnection("EngineIQ.API.Jira");
    }

    public void Dispose()
    {
        _connectLock.Dispose();
        try
        {
            if (_connection is { IsOpen: true })
                _connection.Close();
        }
        catch
        {
            // ignore close errors on shutdown
        }

        _connection?.Dispose();
        _connection = null;
    }
}
