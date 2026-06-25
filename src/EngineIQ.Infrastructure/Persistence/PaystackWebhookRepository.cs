using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EngineIQ.Infrastructure.Persistence;

public sealed class PaystackWebhookRepository : IPaystackWebhookRepository
{
    private readonly IDbContextFactory<EngineIQDbContext> _factory;

    public PaystackWebhookRepository(IDbContextFactory<EngineIQDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<bool> TryClaimEventAsync(
        string eventKey,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = new PaystackWebhookEvent
        {
            EventKey = eventKey.Trim(),
            EventType = eventType.Trim(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        db.PaystackWebhookEvents.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
    }

    public async Task<Guid?> FindTenantIdByPaystackCustomerCodeAsync(
        string customerCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
            return null;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var id = await db.Tenants.AsNoTracking()
            .Where(t => t.PaystackCustomerCode == customerCode.Trim())
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }

    public async Task<Guid?> FindTenantIdByPaystackSubscriptionCodeAsync(
        string subscriptionCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionCode))
            return null;

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var id = await db.Tenants.AsNoTracking()
            .Where(t => t.PaystackSubscriptionCode == subscriptionCode.Trim())
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }

    public async Task<bool> ApplyBillingWebhookAsync(
        Guid tenantId,
        string billingStatus,
        bool suspendTenant,
        string? paystackSubscriptionCode,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.SetCurrentTenantAsync(tenantId, cancellationToken);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return false;

        if (InternalTenantBilling.BypassesPaystack(tenant.BillingStatus))
            return false;

        tenant.BillingStatus = billingStatus.Trim();
        if (!string.IsNullOrWhiteSpace(paystackSubscriptionCode))
            tenant.PaystackSubscriptionCode = paystackSubscriptionCode.Trim();

        if (suspendTenant)
            tenant.Status = "Suspended";
        else if (string.Equals(billingStatus, BillingStatuses.Active, StringComparison.OrdinalIgnoreCase))
            tenant.Status = "Active";

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            return true;

        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }
}
