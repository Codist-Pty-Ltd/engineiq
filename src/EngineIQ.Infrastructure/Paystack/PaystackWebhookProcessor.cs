using System.Text.Json;
using EngineIQ.Domain.Billing;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EngineIQ.Infrastructure.Paystack;

public sealed class PaystackWebhookProcessor : IPaystackWebhookProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPaystackWebhookRepository _webhooks;
    private readonly ILogger<PaystackWebhookProcessor> _logger;

    public PaystackWebhookProcessor(
        IPaystackWebhookRepository webhooks,
        ILogger<PaystackWebhookProcessor> logger)
    {
        _webhooks = webhooks;
        _logger = logger;
    }

    public async Task ProcessAsync(string rawJsonBody, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(rawJsonBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("event", out var eventProp))
        {
            _logger.LogWarning("Paystack webhook missing event type.");
            return;
        }

        var eventType = eventProp.GetString() ?? string.Empty;
        if (!root.TryGetProperty("data", out var data))
        {
            _logger.LogWarning("Paystack webhook {EventType} missing data.", eventType);
            return;
        }

        var eventKey = PaystackWebhookEventKey.Resolve(eventType, data);
        if (!await _webhooks.TryClaimEventAsync(eventKey, eventType, cancellationToken))
        {
            _logger.LogDebug("Duplicate Paystack webhook {EventKey} ignored.", eventKey);
            return;
        }

        var tenantId = await ResolveTenantIdAsync(data, cancellationToken);
        if (tenantId is null)
        {
            _logger.LogWarning("Paystack webhook {EventType} could not resolve tenant.", eventType);
            return;
        }

        var (billingStatus, suspend) = PaystackBillingEventMapper.Map(eventType, data);
        if (billingStatus is null)
        {
            _logger.LogDebug("Paystack webhook {EventType} ignored after claim.", eventType);
            return;
        }

        var subscriptionCode = PaystackWebhookPayloadReader.ReadSubscriptionCode(data);
        var applied = await _webhooks.ApplyBillingWebhookAsync(
            tenantId.Value,
            billingStatus,
            suspend,
            subscriptionCode,
            cancellationToken);

        if (!applied)
        {
            _logger.LogInformation(
                "Paystack webhook {EventType} skipped tenant {TenantId} (internal or missing).",
                eventType,
                tenantId);
            return;
        }

        _logger.LogInformation(
            "Paystack webhook {EventType} applied billing_status={BillingStatus} suspend={Suspend} tenant={TenantId}.",
            eventType,
            billingStatus,
            suspend,
            tenantId);
    }

    private async Task<Guid?> ResolveTenantIdAsync(JsonElement data, CancellationToken cancellationToken)
    {
        var subscriptionCode = PaystackWebhookPayloadReader.ReadSubscriptionCode(data);
        if (!string.IsNullOrWhiteSpace(subscriptionCode))
        {
            var bySub = await _webhooks.FindTenantIdByPaystackSubscriptionCodeAsync(subscriptionCode, cancellationToken);
            if (bySub is not null)
                return bySub;
        }

        var customerCode = PaystackWebhookPayloadReader.ReadCustomerCode(data);
        if (!string.IsNullOrWhiteSpace(customerCode))
        {
            return await _webhooks.FindTenantIdByPaystackCustomerCodeAsync(customerCode, cancellationToken);
        }

        return null;
    }
}

internal static class PaystackWebhookEventKey
{
    public static string Resolve(string eventType, JsonElement data)
    {
        if (data.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            return $"{eventType}:{id}";

        var reference = PaystackWebhookPayloadReader.ReadString(data, "reference");
        if (!string.IsNullOrWhiteSpace(reference))
            return $"{eventType}:{reference}";

        var subscriptionCode = PaystackWebhookPayloadReader.ReadSubscriptionCode(data);
        if (!string.IsNullOrWhiteSpace(subscriptionCode))
            return $"{eventType}:{subscriptionCode}";

        return $"{eventType}:{data.GetRawText().GetHashCode():x}";
    }
}

internal static class PaystackWebhookPayloadReader
{
    public static string? ReadCustomerCode(JsonElement data)
    {
        var direct = ReadString(data, "customer_code");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (data.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.Object)
        {
            var nested = ReadString(customer, "customer_code");
            if (!string.IsNullOrWhiteSpace(nested))
                return nested;
        }

        if (data.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.Object)
        {
            var fromSub = ReadCustomerCode(subscription);
            if (!string.IsNullOrWhiteSpace(fromSub))
                return fromSub;
        }

        return null;
    }

    public static string? ReadSubscriptionCode(JsonElement data)
    {
        var direct = ReadString(data, "subscription_code");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (data.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.Object)
            return ReadString(subscription, "subscription_code");

        if (data.TryGetProperty("authorization", out var authorization) && authorization.ValueKind == JsonValueKind.Object)
            return ReadString(authorization, "subscription_code");

        return null;
    }

    public static string? ReadString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null,
        };
    }
}
