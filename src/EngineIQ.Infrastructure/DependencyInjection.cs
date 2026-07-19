using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Trust;
using EngineIQ.Infrastructure.Email;
using EngineIQ.Infrastructure.Jira;
using EngineIQ.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EngineIQ.Infrastructure;

public static class DependencyInjection
{
    /// <summary>EF Core, repositories, and PostgreSQL (migrations apply RLS).</summary>
    public static IServiceCollection AddEngineIQPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        services.Configure<TrustOptions>(configuration.GetSection(TrustOptions.SectionName));

        var connectionString = configuration.GetSection(PostgresOptions.SectionName)["ConnectionString"]
            ?? string.Empty;

        services.AddDbContextFactory<EngineIQDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(EngineIQDbContext).Assembly.GetName().Name!))
                .UseSnakeCaseNamingConvention();
        });

        // Scoped DbContext for Data Protection key ring persistence.
        services.AddDbContext<EngineIQDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(EngineIQDbContext).Assembly.GetName().Name!))
                .UseSnakeCaseNamingConvention();
        });
        services.AddDataProtection()
            .PersistKeysToDbContext<EngineIQDbContext>()
            .SetApplicationName("EngineIQ");

        services.AddSingleton<IJiraApiTokenProtector, JiraApiTokenProtector>();
        services.AddSingleton<IJobRepository, JobRepository>();
        services.AddSingleton<IFindingRepository, FindingRepository>();
        services.AddSingleton<ITenantMetricsRepository, TenantMetricsRepository>();
        services.AddSingleton<ITenantRepository, TenantRepository>();
        services.AddSingleton<IJiraConnectionRepository, JiraConnectionRepository>();
        services.AddSingleton<IIssueAnalysisJobRepository, IssueAnalysisJobRepository>();

        return services;
    }

    /// <summary>Paystack billing client and tenant subscription orchestration.</summary>
    public static IServiceCollection AddEngineIQPaystack(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Paystack.PaystackOptions>(configuration.GetSection(Paystack.PaystackOptions.SectionName));
        services.AddHttpClient<IPaystackClient, Paystack.PaystackClient>();
        services.AddScoped<ITenantBillingService, Paystack.TenantBillingService>();
        services.AddSingleton<IPaystackWebhookRepository, Persistence.PaystackWebhookRepository>();
        services.AddSingleton<IPaystackWebhookProcessor, Paystack.PaystackWebhookProcessor>();
        return services;
    }

    public static IServiceCollection AddEngineIQEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SendGridOptions>(configuration.GetSection(SendGridOptions.SectionName));
        services.AddHttpClient("SendGrid", client => client.BaseAddress = new Uri("https://api.sendgrid.com/"));
        services.AddSingleton<IEmailNotificationService, SendGridEmailNotificationService>();
        return services;
    }

    /// <summary>RabbitMQ publishers for PR review and Jira issue analysis jobs.</summary>
    public static IServiceCollection AddRabbitMqJobPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IPullReviewJobPublisher, RabbitMqPullReviewJobPublisher>();
        services.AddSingleton<IJiraIssueAnalysisJobPublisher, RabbitMqJiraJobPublisher>();
        return services;
    }

    /// <summary>Redis connection and repo-context cache (Worker).</summary>
    public static IServiceCollection AddEngineIQRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>().Value;
            var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
                ? "localhost:6379"
                : options.ConnectionString;
            return ConnectionMultiplexer.Connect(connectionString);
        });

        services.AddSingleton<IRepoContextCache, Caching.RepoContextRedisCache>();
        return services;
    }
}
