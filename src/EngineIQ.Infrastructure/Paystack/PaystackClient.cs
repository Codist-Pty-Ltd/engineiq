using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Infrastructure.Paystack;

public sealed class PaystackClient : IPaystackClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly PaystackOptions _options;
    private readonly ILogger<PaystackClient> _logger;

    public PaystackClient(HttpClient http, IOptions<PaystackOptions> options, ILogger<PaystackClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri("https://api.paystack.co/");
        if (!string.IsNullOrWhiteSpace(_options.SecretKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<string> CreateCustomerAsync(
        string email,
        string firstName,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["first_name"] = firstName,
        };

        var data = await PostAsync<PaystackCustomerData>("/customer", payload, cancellationToken);
        if (string.IsNullOrWhiteSpace(data.CustomerCode))
            throw new InvalidOperationException("Paystack customer response missing customer_code.");

        return data.CustomerCode;
    }

    public async Task<PaystackInitializeResult> InitializeSubscriptionCheckoutAsync(
        string email,
        string paystackPlanCode,
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["plan"] = paystackPlanCode,
            ["callback_url"] = callbackUrl,
        };

        var data = await PostAsync<PaystackInitializeData>("/transaction/initialize", payload, cancellationToken);
        if (string.IsNullOrWhiteSpace(data.AuthorizationUrl) || string.IsNullOrWhiteSpace(data.Reference))
            throw new InvalidOperationException("Paystack initialize response missing authorization_url or reference.");

        return new PaystackInitializeResult(data.Reference, data.AuthorizationUrl);
    }

    public async Task<PaystackVerifyTransactionResult> VerifyTransactionAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var envelope = await GetEnvelopeAsync<PaystackVerifyData>(
            $"/transaction/verify/{Uri.EscapeDataString(reference)}",
            cancellationToken);

        if (!envelope.Status || envelope.Data is null)
        {
            return new PaystackVerifyTransactionResult(false, null, null, null);
        }

        var tx = envelope.Data;
        var success = string.Equals(tx.Status, "success", StringComparison.OrdinalIgnoreCase);
        return new PaystackVerifyTransactionResult(
            success,
            tx.Authorization?.SubscriptionCode,
            tx.Customer?.CustomerCode,
            tx.PlanObject?.PlanCode ?? tx.Plan);
    }

    public async Task<string> UpdateSubscriptionPlanAsync(
        string subscriptionCode,
        string paystackPlanCode,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?> { ["plan"] = paystackPlanCode };
        var data = await PutAsync<PaystackSubscriptionData>(
            $"/subscription/{Uri.EscapeDataString(subscriptionCode)}",
            payload,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(data.SubscriptionCode))
            throw new InvalidOperationException("Paystack subscription update missing subscription_code.");

        return data.SubscriptionCode;
    }

    private async Task<TData> PostAsync<TData>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var envelope = await PostEnvelopeAsync<TData>(path, body, cancellationToken);
        EnsureSuccess(envelope, path);
        return envelope.Data ?? throw new InvalidOperationException($"Paystack {path} returned empty data.");
    }

    private async Task<TData> PutAsync<TData>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var envelope = await PutEnvelopeAsync<TData>(path, body, cancellationToken);
        EnsureSuccess(envelope, path);
        return envelope.Data ?? throw new InvalidOperationException($"Paystack {path} returned empty data.");
    }

    private async Task<PaystackEnvelope<TData>> PostEnvelopeAsync<TData>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadEnvelopeAsync<TData>(response, path, cancellationToken);
    }

    private async Task<PaystackEnvelope<TData>> PutEnvelopeAsync<TData>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(path, body, JsonOptions, cancellationToken);
        return await ReadEnvelopeAsync<TData>(response, path, cancellationToken);
    }

    private async Task<PaystackEnvelope<TData>> GetEnvelopeAsync<TData>(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        return await ReadEnvelopeAsync<TData>(response, path, cancellationToken);
    }

    private async Task<PaystackEnvelope<TData>> ReadEnvelopeAsync<TData>(
        HttpResponseMessage response,
        string path,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        PaystackEnvelope<TData>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PaystackEnvelope<TData>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Paystack {Path} returned non-JSON ({StatusCode}).", path, (int)response.StatusCode);
            throw new InvalidOperationException($"Paystack {path} returned invalid JSON.", ex);
        }

        if (envelope is null)
            throw new InvalidOperationException($"Paystack {path} returned empty body.");

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Paystack {Path} HTTP {StatusCode}: {Message}",
                path,
                (int)response.StatusCode,
                envelope.Message);
        }

        return envelope;
    }

    private static void EnsureSuccess<TData>(PaystackEnvelope<TData> envelope, string path)
    {
        if (envelope.Status)
            return;

        throw new InvalidOperationException(envelope.Message ?? $"Paystack {path} failed.");
    }

    private sealed class PaystackEnvelope<T>
    {
        public bool Status { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    private sealed class PaystackCustomerData
    {
        [JsonPropertyName("customer_code")]
        public string CustomerCode { get; set; } = string.Empty;
    }

    private sealed class PaystackInitializeData
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class PaystackVerifyData
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("plan")]
        public string? Plan { get; set; }

        [JsonPropertyName("plan_object")]
        public PaystackPlanRef? PlanObject { get; set; }

        [JsonPropertyName("customer")]
        public PaystackCustomerRef? Customer { get; set; }

        [JsonPropertyName("authorization")]
        public PaystackAuthorizationRef? Authorization { get; set; }
    }

    private sealed class PaystackPlanRef
    {
        [JsonPropertyName("plan_code")]
        public string PlanCode { get; set; } = string.Empty;
    }

    private sealed class PaystackCustomerRef
    {
        [JsonPropertyName("customer_code")]
        public string CustomerCode { get; set; } = string.Empty;
    }

    private sealed class PaystackAuthorizationRef
    {
        [JsonPropertyName("subscription_code")]
        public string SubscriptionCode { get; set; } = string.Empty;
    }

    private sealed class PaystackSubscriptionData
    {
        [JsonPropertyName("subscription_code")]
        public string SubscriptionCode { get; set; } = string.Empty;
    }
}
