using EngineIQ.AIEngine;
using EngineIQ.AIEngine.IssueImprovement;
using EngineIQ.ContextBuilder;
using EngineIQ.Domain.Configuration;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Trust;
using EngineIQ.GitHub;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Email;
using EngineIQ.Jira;
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
builder.Services.Configure<PendingPublishRelayOptions>(builder.Configuration.GetSection(PendingPublishRelayOptions.SectionName));
builder.Services.Configure<EngineIQDashboardOptions>(builder.Configuration.GetSection(EngineIQDashboardOptions.SectionName));
builder.Services.Configure<JiraClientOptions>(builder.Configuration.GetSection(JiraClientOptions.SectionName));

builder.Services.AddEngineIQObservability(builder.Configuration, "engineiq-worker");
builder.Services.AddEngineIQPersistence(builder.Configuration);
builder.Services.AddRabbitMqJobPublisher(builder.Configuration);
builder.Services.AddEngineIQEmail(builder.Configuration);
builder.Services.AddEngineIQRedis(builder.Configuration);
builder.Services.AddEngineIQEmbeddings(builder.Configuration);
builder.Services.Configure<RedisContextOptions>(builder.Configuration.GetSection(RedisContextOptions.SectionName));
builder.Services.Configure<EngineIQ.ContextBuilder.Indexing.IndexingOptions>(
    builder.Configuration.GetSection(EngineIQ.ContextBuilder.Indexing.IndexingOptions.SectionName));
builder.Services.Configure<EngineIQ.ContextBuilder.Search.RetrievalOptions>(
    builder.Configuration.GetSection(EngineIQ.ContextBuilder.Search.RetrievalOptions.SectionName));

builder.Services.AddHttpClient(ReviewService.AnthropicHttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
});

builder.Services.AddHttpClient(JiraCloudClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JiraClientOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
    client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
});

builder.Services.AddSingleton<GitHubInstallationAuthenticator>();
builder.Services.AddSingleton<IGitHubClient, GitHubAppClient>();
builder.Services.AddSingleton<IRepoArchiveClient, RepoArchiveClient>();
builder.Services.AddSingleton<IJiraClient, JiraCloudClient>();
builder.Services.AddSingleton<IStandardsEngine, EngineIQ.StandardsEngine.StandardsEngine>();
builder.Services.AddSingleton<IContextBuilder, ContextBuilderService>();
builder.Services.AddSingleton<ICodeSearchService, EngineIQ.ContextBuilder.Search.CodeSearchService>();
builder.Services.AddSingleton<IAIEngine, ReviewService>();
builder.Services.AddSingleton<IJiraIssueImprovementService, IssueImprovementService>();
builder.Services.AddSingleton<IReviewOrchestrator, ReviewOrchestrator>();
builder.Services.AddSingleton<IIssueAnalysisOrchestrator, IssueAnalysisOrchestrator>();
builder.Services.AddSingleton<ICodeChunker, EngineIQ.ContextBuilder.Indexing.CompositeCodeChunker>();
builder.Services.AddSingleton<IRepoIndexer, EngineIQ.ContextBuilder.Indexing.RepoIndexer>();
builder.Services.AddSingleton<IBackfillPacer, TaskBackfillPacer>();

builder.Services.AddHostedService<PullReviewJobConsumer>();
builder.Services.AddHostedService<JiraIssueJobConsumer>();
builder.Services.AddHostedService<RepoIndexJobConsumer>();
builder.Services.AddHostedService<BacklogBackfillConsumer>();
builder.Services.AddHostedService<PendingPublishRelayService>();
builder.Services.AddHostedService<RabbitMqQueueDepthCollector>();

var app = builder.Build();
app.MapEngineIQMetricsEndpoint();
await app.RunAsync();
