using EngineIQ.AIEngine;
using EngineIQ.Domain.Configuration;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Trust;
using EngineIQ.GitHub;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Email;
using EngineIQ.ReviewEngine.Orchestration;
using EngineIQ.Worker;
var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GitHubClientOptions>(builder.Configuration.GetSection(GitHubClientOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<TrustOptions>(builder.Configuration.GetSection(TrustOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<EngineIQDashboardOptions>(builder.Configuration.GetSection(EngineIQDashboardOptions.SectionName));

builder.Services.AddEngineIQPersistence(builder.Configuration);
builder.Services.AddEngineIQEmail(builder.Configuration);

builder.Services.AddHttpClient(ReviewService.AnthropicHttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});

builder.Services.AddSingleton<IGitHubClient, GitHubAppClient>();
builder.Services.AddSingleton<IAIEngine, ReviewService>();
builder.Services.AddSingleton<IReviewOrchestrator, ReviewOrchestrator>();

builder.Services.AddHostedService<PullReviewJobConsumer>();

var host = builder.Build();
host.Run();
