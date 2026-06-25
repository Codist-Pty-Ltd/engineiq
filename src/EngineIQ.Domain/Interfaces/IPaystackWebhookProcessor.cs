namespace EngineIQ.Domain.Interfaces;

public interface IPaystackWebhookProcessor
{
    /// <summary>Idempotent billing side-effects for a validated Paystack webhook JSON body.</summary>
    Task ProcessAsync(string rawJsonBody, CancellationToken cancellationToken = default);
}

public sealed record PaystackWebhookProcessResult(
    bool Processed,
    bool Duplicate,
    string? EventKey,
    string? EventType);
