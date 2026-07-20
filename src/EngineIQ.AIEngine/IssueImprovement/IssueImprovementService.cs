using System.Text;
using System.Text.Json;
using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Context;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using EngineIQ.Domain.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.AIEngine.IssueImprovement;

public sealed class IssueImprovementService : IJiraIssueImprovementService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;
    private readonly ILogger<IssueImprovementService> _logger;

    public IssueImprovementService(
        IHttpClientFactory httpClientFactory,
        IOptions<AnthropicOptions> options,
        ILogger<IssueImprovementService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(IssueImprovementResult Result, int InputTokens, int OutputTokens, decimal EstimatedCostZar)> ImproveAsync(
        JiraIssueDetails issue,
        CodeSearchResult? codeContext = null,
        RepoContext? repoContext = null,
        JiraParentSummary? parent = null,
        CancellationToken cancellationToken = default)
    {
        var hasCode = codeContext is { IsEmpty: false };
        var systemPrompt = IssueImprovementPromptBuilder.BuildSystemPrompt(issue.IssueType, hasCode);
        var userContent = IssueImprovementPromptBuilder.BuildUserPrompt(issue, codeContext, repoContext, parent);

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

        var client = _httpClientFactory.CreateClient(ReviewService.AnthropicHttpClientName);
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
            _logger.LogWarning(
                "Anthropic API error Status={Status} BodyLength={Length} IssueKey={IssueKey}",
                (int)response.StatusCode,
                responseText.Length,
                issue.IssueKey);
            throw new InvalidOperationException("Anthropic Messages API request failed.");
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        if (!AnthropicReviewResponseParser.TryParseAssistantText(root, out var assistantText))
            throw new IssueImprovementParseException("missing_assistant_text");

        var result = IssueImprovementResponseParser.Parse(assistantText);
        if (!hasCode && result.ImpactAnalysis is not null)
            result = result with { ImpactAnalysis = null };

        _ = AnthropicReviewResponseParser.TryParseUsage(root, out var inputTokens, out var outputTokens);
        var estimatedZar = AnthropicReviewResponseParser.EstimateZarCost(
            inputTokens,
            outputTokens,
            _options.InputUsdPerMillionTokens,
            _options.OutputUsdPerMillionTokens,
            _options.UsdToZar);

        _logger.LogInformation(
            "IssueImprovementCompleted IssueKey={IssueKey} EstimatedZarCost={EstimatedZar:F4} InputTokens={InputTokens} OutputTokens={OutputTokens} HasImpact={HasImpact}",
            issue.IssueKey,
            estimatedZar,
            inputTokens,
            outputTokens,
            result.ImpactAnalysis is not null);

        return (result, inputTokens, outputTokens, estimatedZar);
    }
}
