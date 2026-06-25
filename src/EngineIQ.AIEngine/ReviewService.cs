using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Reviews;
using EngineIQ.Domain.Tenants;
using EngineIQ.Domain.Trust;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.AIEngine;

public class ReviewService : IAIEngine
{
    public const string AnthropicHttpClientName = "Anthropic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;
    private readonly TrustOptions _trust;
    private readonly ILogger<ReviewService> _logger;

    /// <summary>System instructions for PR review (unchanged behaviour vs prior OpenAI path).</summary>
    internal const string SystemPrompt = @"You are an expert code reviewer for a .NET / C# codebase. Review the provided unified diff and respond with a concise PR review comment in Markdown.

Focus on:
- Architecture and layering (e.g. domain depending on infrastructure)
- Obvious bugs, null handling, async misuse
- Security (hardcoded secrets, sensitive data)
- Readability and maintainability

Keep the review actionable and friendly. Use bullet points. Do not include the diff in your response — only your review text.";

    public ReviewService(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        IOptions<TrustOptions> trust,
        ILogger<ReviewService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _trust = trust.Value;
        _logger = logger;
    }

    public async Task<PrReviewDiffOutcome> ReviewDiffAsync(
        string diff,
        TenantPortalPreferences? preferences = null,
        string? standardsConfigYaml = null,
        RepoContext? repoContext = null,
        CancellationToken cancellationToken = default)
    {
        var prefs = preferences ?? new TenantPortalPreferences();
        var systemPrompt = ReviewPromptBuilder.BuildSystemPrompt(prefs, standardsConfigYaml, repoContext);
        var footer = AnthropicReviewResponseParser.BuildTrustFooter(_trust.PublicApiBaseUrl);
        if (string.IsNullOrWhiteSpace(diff))
        {
            var emptyBody = "_No changes to review._" + footer;
            return new PrReviewDiffOutcome(emptyBody, 0, 0, 0m, 0, Array.Empty<FindingWriteDto>());
        }

        var userContent = $"Review this pull request diff:\n\n```diff\n{diff}\n```";

        var body = new
        {
            model = string.IsNullOrWhiteSpace(_options.Model) ? "claude-sonnet-4-6" : _options.Model,
            max_tokens = _options.MaxOutputTokens,
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = userContent }
            }
        };

        var client = _httpClientFactory.CreateClient(AnthropicHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        if (!request.Headers.Contains("anthropic-version"))
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API error Status={Status} BodyLength={Length}", (int)response.StatusCode, responseText.Length);
            throw new InvalidOperationException("Anthropic Messages API request failed.");
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        if (!AnthropicReviewResponseParser.TryParseAssistantText(root, out var reviewText) || string.IsNullOrWhiteSpace(reviewText))
            reviewText = "_No review generated._";

        reviewText = reviewText.Trim();
        var parsedFindings = AnthropicReviewResponseParser.ParseFindingsFromMarkdown(reviewText);
        var findingsEstimate = parsedFindings.Count > 0
            ? parsedFindings.Count
            : AnthropicReviewResponseParser.EstimateBulletFindingCount(reviewText);

        _ = AnthropicReviewResponseParser.TryParseUsage(root, out var inputTokens, out var outputTokens);
        var estimatedZar = AnthropicReviewResponseParser.EstimateZarCost(
            inputTokens,
            outputTokens,
            _options.InputUsdPerMillionTokens,
            _options.OutputUsdPerMillionTokens,
            _options.UsdToZar);

        _logger.LogInformation(
            "AnthropicReviewCompleted EstimatedZarCost={EstimatedZar:F4} InputTokens={InputTokens} OutputTokens={OutputTokens} Model={Model}",
            estimatedZar,
            inputTokens,
            outputTokens,
            _options.Model);

        return new PrReviewDiffOutcome(
            reviewText + footer,
            inputTokens,
            outputTokens,
            estimatedZar,
            findingsEstimate,
            parsedFindings);
    }
}
