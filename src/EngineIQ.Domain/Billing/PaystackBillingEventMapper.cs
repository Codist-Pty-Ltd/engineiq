using System.Text.Json;
using EngineIQ.Domain.Billing;

namespace EngineIQ.Domain.Billing;

public static class PaystackBillingEventMapper
{
    public static (string? BillingStatus, bool SuspendTenant) Map(string eventType, JsonElement data)
    {
        return eventType.Trim().ToLowerInvariant() switch
        {
            "charge.success" => (BillingStatuses.Active, false),
            "subscription.create" => (BillingStatuses.Active, false),
            "subscription.disable" => (BillingStatuses.Cancelled, true),
            "invoice.payment_failed" => (BillingStatuses.PastDue, true),
            "invoice.update" => MapInvoiceUpdate(data),
            _ => (null, false),
        };
    }

    private static (string? BillingStatus, bool SuspendTenant) MapInvoiceUpdate(JsonElement data)
    {
        var status = ReadString(data, "status")?.ToLowerInvariant();
        return status switch
        {
            "success" or "paid" => (BillingStatuses.Active, false),
            "failed" => (BillingStatuses.PastDue, true),
            _ => (null, false),
        };
    }

    private static string? ReadString(JsonElement obj, string propertyName)
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
