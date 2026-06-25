using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace EngineIQ.Infrastructure.Telemetry;

public static class TracePropagation
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static void Inject(IBasicProperties properties)
    {
        properties.Headers ??= new Dictionary<string, object>();
        Propagator.Inject(
            new PropagationContext(Activity.Current?.Context ?? default, Baggage.Current),
            properties.Headers,
            static (headers, key, value) => headers[key] = value);
    }

    public static ActivityContext Extract(IBasicProperties? properties)
    {
        if (properties?.Headers is null || properties.Headers.Count == 0)
            return default;

        return Propagator.Extract(
            default,
            properties.Headers,
            static (headers, key) =>
            {
                if (!headers.TryGetValue(key, out var value))
                    return [];

                return value switch
                {
                    byte[] bytes => [System.Text.Encoding.UTF8.GetString(bytes)],
                    string s => [s],
                    _ => [],
                };
            }).ActivityContext;
    }
}
