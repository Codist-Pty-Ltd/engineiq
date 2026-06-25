namespace EngineIQ.Infrastructure.Persistence.Entities;

/// <summary>Idempotency ledger for Paystack webhook deliveries (not tenant-scoped).</summary>
public sealed class PaystackWebhookEvent
{
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}
