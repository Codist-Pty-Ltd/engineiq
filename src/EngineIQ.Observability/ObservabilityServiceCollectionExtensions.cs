using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EngineIQ.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddEngineIQObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.Configure<ObservabilityOptions>(configuration.GetSection(ObservabilityOptions.SectionName));
        var opts = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                   ?? new ObservabilityOptions();

        if (!opts.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(ReviewTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(ReviewTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        services.AddSingleton(ReviewTelemetry.ActivitySource);
        services.AddSingleton(ReviewTelemetry.Meter);

        return services;
    }

    public static WebApplication UseEngineIQMetricsPortGate(this WebApplication app)
    {
        var opts = app.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                   ?? new ObservabilityOptions();

        if (!opts.Enabled)
            return app;

        app.UseMiddleware<MetricsPortGateMiddleware>(opts.MetricsPort);
        return app;
    }

    public static WebApplication MapEngineIQMetricsEndpoint(this WebApplication app)
    {
        var opts = app.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                   ?? new ObservabilityOptions();

        if (!opts.Enabled)
            return app;

        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }

    public static WebApplicationBuilder ConfigureEngineIQMetricsKestrel(
        this WebApplicationBuilder builder,
        int defaultMetricsPort)
    {
        var opts = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                   ?? new ObservabilityOptions { MetricsPort = defaultMetricsPort };

        if (!opts.Enabled)
            return builder;

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            var addresses = context.Configuration["ASPNETCORE_URLS"]
                ?? context.Configuration[WebHostDefaults.ServerUrlsKey]
                ?? "http://+:5000";

            foreach (var address in addresses.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var endpoint = BindingAddress.Parse(address);
                if (endpoint.Host is "+" or "*")
                    kestrel.ListenAnyIP(endpoint.Port);
                else if (endpoint.Host is "localhost" or "127.0.0.1" or "[::1]")
                    kestrel.ListenLocalhost(endpoint.Port);
                else
                    kestrel.Listen(System.Net.IPAddress.Parse(endpoint.Host), endpoint.Port);
            }

            kestrel.ListenLocalhost(opts.MetricsPort);
        });

        return builder;
    }
}
