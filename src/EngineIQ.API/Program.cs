using System.Threading.RateLimiting;
using EngineIQ.API.Cors;
using EngineIQ.API.Middleware;
using EngineIQ.API.Options;
using EngineIQ.API.Validation;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Trust;
using EngineIQ.GitHub;
using EngineIQ.Infrastructure;
using EngineIQ.Jira;
using EngineIQ.Observability;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureEngineIQMetricsKestrel(defaultMetricsPort: 9464);
builder.Services.AddEngineIQObservability(builder.Configuration, "engineiq-api");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.Configure<GitHubClientOptions>(builder.Configuration.GetSection(GitHubClientOptions.SectionName));
builder.Services.Configure<EngineIQAppOptions>(builder.Configuration.GetSection(EngineIQAppOptions.SectionName));
builder.Services.Configure<TrustOptions>(builder.Configuration.GetSection(TrustOptions.SectionName));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<JiraClientOptions>(builder.Configuration.GetSection(JiraClientOptions.SectionName));

var corsOrigins = CorsOriginResolver.Resolve(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        PortalCorsExtensions.PolicyName,
        policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromHours(1)));
});

builder.Services.AddEngineIQPersistence(builder.Configuration);
builder.Services.AddEngineIQPaystack(builder.Configuration);
builder.Services.AddEngineIQEmail(builder.Configuration);
builder.Services.AddRabbitMqJobPublisher(builder.Configuration);

builder.Services.AddHttpClient(JiraCloudClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JiraClientOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
    client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
});
builder.Services.AddSingleton<IJiraClient, JiraCloudClient>();
builder.Services.AddSingleton<JiraWebhookValidator>();
builder.Services.AddSingleton<GitHubInstallationAuthenticator>();
builder.Services.AddSingleton<IRepoArchiveClient, RepoArchiveClient>();

builder.Services.AddSingleton<StandardsConfigYamlValidator>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (_, _) => ValueTask.CompletedTask;

    options.AddPolicy("onboarding", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKeyFromIp(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("tenantApi", httpContext =>
    {
        var key = httpContext.Items.TryGetValue("TenantId", out var v) && v is Guid g
            ? g.ToString("N", null)
            : "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// TLS terminates at Caddy; forwarded proto avoids redirect loops that break CORS preflight.
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();
app.UseCors(PortalCorsExtensions.PolicyName);
app.UseMiddleware<ApiKeyTenantMiddleware>();
app.UseRateLimiter();
app.UseEngineIQMetricsPortGate();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).RequireCors(PortalCorsExtensions.PolicyName);
app.MapControllers().RequireCors(PortalCorsExtensions.PolicyName);
app.MapEngineIQMetricsEndpoint();

app.Run();

static string PartitionKeyFromIp(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
