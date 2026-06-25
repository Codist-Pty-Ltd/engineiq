using System.Text.Json.Serialization;
using EngineIQ.Admin.Services;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure;
using EngineIQ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EngineIQ.Admin;

public static class AdminApiEndpoints
{
    public static void MapAdminApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin");

        group.MapGet("/health", async (
                IDbContextFactory<EngineIQDbContext> dbFactory,
                IOptions<RabbitMqOptions> mq,
                CancellationToken cancellationToken) =>
            {
                string dbStatus;
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                    dbStatus = await db.Database.CanConnectAsync(cancellationToken) ? "ok" : "error";
                }
                catch
                {
                    dbStatus = "error";
                }

                string mqStatus;
                try
                {
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(mq.Value.ConnectionString),
                        DispatchConsumersAsync = true
                    };
                    using var conn = factory.CreateConnection("EngineIQ.Admin.Health");
                    mqStatus = conn.IsOpen ? "ok" : "error";
                }
                catch
                {
                    mqStatus = "error";
                }

                return Results.Ok(new { database = dbStatus, rabbitMq = mqStatus });
            })
            .WithName("AdminHealth");

        group.MapGet("/metrics", async (AdminPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetPlatformMetricsAsync(ct)))
            .WithName("AdminMetrics");

        group.MapGet("/tenants", async (AdminPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListTenantsAsync(ct)))
            .WithName("AdminTenants");

        group.MapGet("/tenants/{tenantId:guid}", async (Guid tenantId, AdminPortalService svc, CancellationToken ct) =>
            {
                var detail = await svc.GetTenantDetailAsync(tenantId, ct);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .WithName("AdminTenantDetail");

        group.MapPost("/tenants/{tenantId:guid}/suspend", async (
                Guid tenantId,
                SuspendTenantBody? body,
                AdminPortalService svc,
                CancellationToken ct) =>
            {
                if (body is null)
                    return Results.BadRequest();
                var ok = await svc.SetTenantSuspendedAsync(tenantId, body.Suspended, ct);
                return ok ? Results.NoContent() : Results.NotFound();
            })
            .WithName("AdminTenantSuspend");

        group.MapPost("/tenants/{tenantId:guid}/plan", async (
                Guid tenantId,
                UpgradePlanBody? body,
                AdminPortalService svc,
                CancellationToken ct) =>
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Plan))
                    return Results.BadRequest();
                var ok = await svc.UpgradeTenantAsync(tenantId, body.Plan, body.FeatureFlagsJson, ct);
                return ok ? Results.NoContent() : Results.NotFound();
            })
            .WithName("AdminTenantPlan");

        group.MapPost("/tenants/{tenantId:guid}/rotate-key", async (
                Guid tenantId,
                ITenantRepository tenants,
                CancellationToken ct) =>
            {
                var (ok, apiKey) = await tenants.RotateApiKeyAsync(tenantId, ct);
                if (!ok)
                    return Results.NotFound();
                return Results.Ok(new RotateApiKeyResponse(apiKey!));
            })
            .WithName("AdminTenantRotateKey");

        group.MapGet("/tenants/{tenantId:guid}/findings", async (
                Guid tenantId,
                int? take,
                AdminPortalService svc,
                CancellationToken ct) =>
                Results.Ok(await svc.ListFindingsAsync(tenantId, take ?? 500, ct)))
            .WithName("AdminTenantFindings");

        group.MapGet("/jobs/failed", async (AdminPortalService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListFailedJobsAsync(ct)))
            .WithName("AdminJobsFailed");

        group.MapPost("/tenants/{tenantId:guid}/jobs/{jobId:guid}/retry", async (
                Guid tenantId,
                Guid jobId,
                AdminPortalService svc,
                CancellationToken ct) =>
            {
                try
                {
                    await svc.RetryFailedDbJobAsync(tenantId, jobId, ct);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("AdminRetryFailedJob");

        group.MapGet("/jobs/dlq", (DlqRetryService dlq) =>
                Results.Ok(dlq.PeekDlqJsonPreviews(100)))
            .WithName("AdminDlqPeek");

        group.MapPost("/jobs/dlq/retry", (DlqRetryService dlq, RetryDlqBody? body) =>
            {
                if (body is null)
                    return Results.BadRequest();
                try
                {
                    var drained = dlq.RetryMessageAtIndex(body.Index);
                    return Results.Ok(new { drainedCount = drained });
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("AdminDlqRetry");
    }

    public sealed record SuspendTenantBody(bool Suspended);

    public sealed record UpgradePlanBody(string Plan, string? FeatureFlagsJson);

    public sealed record RetryDlqBody(int Index);

    public sealed record RotateApiKeyResponse([property: JsonPropertyName("api_key")] string ApiKey);
}
