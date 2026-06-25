namespace EngineIQ.Domain.Interfaces;

public interface IPaystackClient
{
    bool IsConfigured { get; }

    Task<string> CreateCustomerAsync(string email, string firstName, CancellationToken cancellationToken = default);

    Task<PaystackInitializeResult> InitializeSubscriptionCheckoutAsync(
        string email,
        string paystackPlanCode,
        string callbackUrl,
        CancellationToken cancellationToken = default);

    Task<PaystackVerifyTransactionResult> VerifyTransactionAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<string> UpdateSubscriptionPlanAsync(
        string subscriptionCode,
        string paystackPlanCode,
        CancellationToken cancellationToken = default);
}

public sealed record PaystackInitializeResult(string Reference, string AuthorizationUrl);

public sealed record PaystackVerifyTransactionResult(
    bool Success,
    string? SubscriptionCode,
    string? CustomerCode,
    string? PlanCode);
