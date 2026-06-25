namespace EngineIQ.Domain.Interfaces;

public interface IPaystackWebhookRepository
{
    /// <summary>Returns true when the event id was newly recorded (first delivery).</summary>
    Task<bool> TryClaimEventAsync(string eventKey, string eventType, CancellationToken cancellationToken = default);

    Task<Guid?> FindTenantIdByPaystackCustomerCodeAsync(string customerCode, CancellationToken cancellationToken = default);

    Task<Guid?> FindTenantIdByPaystackSubscriptionCodeAsync(string subscriptionCode, CancellationToken cancellationToken = default);

  /// <summary>
    /// Updates billing status and optionally mirrors to tenant <c>Status</c> (Suspended / Active).
    /// Returns false when tenant is missing or <see cref="Billing.InternalTenantBilling.Internal"/> (no-op).
    /// </summary>
    Task<bool> ApplyBillingWebhookAsync(
        Guid tenantId,
        string billingStatus,
        bool suspendTenant,
        string? paystackSubscriptionCode,
        CancellationToken cancellationToken = default);
}
