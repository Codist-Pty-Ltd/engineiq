using EngineIQ.AIEngine;
using EngineIQ.ContextBuilder;
using EngineIQ.Domain.Configuration;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Trust;
using EngineIQ.GitHub;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Email;
using EngineIQ.Observability;
using EngineIQ.ReviewEngine.Orchestration;
using EngineIQ.Worker;

var builder = WebApplication.CreateBuilder(args);

var workerMetricsPort = builder.Configuration.GetValue("Observability:MetricsPort", 9465);
builder.WebHost.UseUrls($"http://127.0.0.1:{workerMetricsPort}");

builder.Services.Configure<GitHubClientOptions>(builder.Configuration.GetSection(GitHubClientOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<TrustOptions>(builder.Configuration.GetSection(TrustOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<EngineIQDashboardOptions>(builder.Configuration.GetSection(EngineIQDashboardOptions.SectionName));

builder.Services.AddEngineIQObservability(builder.Configuration, "engineiq-worker");
builder.Services.AddEngineIQPersistence(builder.Configuration);
builder.Services.AddEngineIQEmail(builder.Configuration);
builder.Services.AddEngineIQRedis(builder.Configuration);
builder.Services.Configure<RedisContextOptions>(builder.Configuration.GetSection(RedisContextOptions.SectionName));

builder.Services.AddHttpClient(ReviewService.AnthropicHttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});

builder.Services.AddSingleton<IGitHubClient, GitHubAppClient>();
builder.Services.AddSingleton<IStandardsEngine, EngineIQ.StandardsEngine.StandardsEngine>();
builder.Services.AddSingleton<IContextBuilder, ContextBuilderService>();
builder.Services.AddSingleton<IAIEngine, ReviewService>();
builder.Services.AddSingleton<IReviewOrchestrator, ReviewOrchestrator>();

builder.Services.AddHostedService<PullReviewJobConsumer>();
builder.Services.AddHostedService<RabbitMqQueueDepthCollector>();

var app = builder.Build();
app.MapEngineIQMetricsEndpoint();
await app.RunAsync();
