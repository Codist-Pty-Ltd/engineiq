using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineIQ.Domain.Indexing;
using EngineIQ.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Infrastructure.Embeddings;

/// <summary>Voyage AI embeddings for code-chunk indexing. Never logs input content or the API key.</summary>
public sealed class VoyageEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly VoyageOptions _options;
    private readonly ILogger<VoyageEmbeddingClient> _logger;

    public VoyageEmbeddingClient(HttpClient httpClient, IOptions<VoyageOptions> options, ILogger<VoyageEmbeddingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public int Dimensions => _options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return Array.Empty<float[]>();

        var maxChars = Math.Max(1, _options.MaxInputChars);
        var truncated = inputs
            .Select(i => i.Length <= maxChars ? i : i[..maxChars])
            .ToList();

        var results = new List<float[]>(truncated.Count);
        var batchSize = Math.Max(1, _options.BatchSize);

        for (var offset = 0; offset < truncated.Count; offset += batchSize)
        {
            var batch = truncated.Skip(offset).Take(batchSize).ToList();
            var batchResults = await EmbedBatchWithRetryAsync(batch, inputType, cancellationToken);
            results.AddRange(batchResults);
        }

        return results;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchWithRetryAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await EmbedBatchAsync(inputs, inputType, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (InvalidOperationException ex) when (ex.Data.Contains("retryable") && (bool)ex.Data["retryable"]!)
            {
                last = ex;
            }

            if (attempt == maxAttempts)
                break;

            var delayMs = (int)(Math.Pow(2, attempt) * 200 + Random.Shared.Next(0, 150));
            _logger.LogWarning(
                "Voyage embeddings batch retry {Attempt}/{Max} after {DelayMs}ms (batchSize={BatchSize}).",
                attempt,
                maxAttempts,
                delayMs,
                inputs.Count);
            await Task.Delay(delayMs, cancellationToken);
        }

        throw last ?? new InvalidOperationException("Voyage embeddings API request failed.");
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> inputs,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken)
    {
        var requestBody = new VoyageEmbeddingsRequest(
            inputs,
            string.IsNullOrWhiteSpace(_options.Model) ? "voyage-code-3" : _options.Model,
            inputType == EmbeddingInputType.Query ? "query" : "document",
            _options.Dimensions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError)
        {
            _logger.LogWarning(
                "Voyage embeddings API retryable Status={Status} BodyLength={Length} BatchSize={BatchSize}",
                (int)response.StatusCode,
                responseText.Length,
                inputs.Count);
            var ex = new InvalidOperationException("Voyage embeddings API request failed (retryable).");
            ex.Data["retryable"] = true;
            throw ex;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Voyage embeddings API error Status={Status} BodyLength={Length} BatchSize={BatchSize}",
                (int)response.StatusCode,
                responseText.Length,
                inputs.Count);
            throw new InvalidOperationException("Voyage embeddings API request failed.");
        }

        var parsed = JsonSerializer.Deserialize<VoyageEmbeddingsResponse>(responseText);
        if (parsed?.Data is null || parsed.Data.Count != inputs.Count)
            throw new InvalidOperationException("Voyage embeddings API returned an unexpected response shape.");

        var totalTokens = parsed.Usage?.TotalTokens;
        _logger.LogInformation(
            "VoyageEmbeddingsCompleted BatchSize={BatchSize} TotalTokens={TotalTokens} Model={Model}",
            inputs.Count,
            totalTokens,
            _options.Model);

        return parsed.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToList();
    }

    private sealed record VoyageEmbeddingsRequest(
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("output_dimension")] int OutputDimension);

    private sealed record VoyageEmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<VoyageEmbeddingDatum> Data,
        [property: JsonPropertyName("usage")] VoyageUsage? Usage);

    private sealed record VoyageEmbeddingDatum(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);

    private sealed record VoyageUsage(
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
