using System.Net;
using System.Text;
using System.Text.Json;
using EngineIQ.AIEngine;
using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Trust;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class ReviewServiceTests
{
    private sealed class StubAnthropicHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubAnthropicHandler(string responseBody) => _responseBody = responseBody;

        public HttpRequestMessage? CapturedRequest { get; private set; }

        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.anthropic.com/")
            };
            return client;
        }
    }

    [Fact]
    public async Task ReviewDiffAsync_posts_messages_shape_and_appends_footer()
    {
        const string anthropicJson = """
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "model": "claude-sonnet-4-6",
              "content": [ { "type": "text", "text": "LGTM on structure." } ],
              "usage": { "input_tokens": 500, "output_tokens": 120 }
            }
            """;

        var handler = new StubAnthropicHandler(anthropicJson);
        var factory = new TestHttpClientFactory(handler);
        var options = Options.Create(new AnthropicOptions
        {
            ApiKey = "sk-test-not-real",
            Model = "claude-sonnet-4-6",
            UsdToZar = 18.5,
            InputUsdPerMillionTokens = 3,
            OutputUsdPerMillionTokens = 15
        });
        var trust = Options.Create(new TrustOptions { PublicApiBaseUrl = "https://api.test.example" });
        var svc = new ReviewService(factory, options, trust, NullLogger<ReviewService>.Instance);

        var result = await svc.ReviewDiffAsync("diff --git a/README.md", cancellationToken: CancellationToken.None);

        Assert.Contains("LGTM on structure.", result.CommentBody);
        Assert.Contains("EngineIQ processed this diff ephemerally", result.CommentBody);
        Assert.Contains("[View our security model](https://api.test.example/security)", result.CommentBody);
        Assert.Equal(500, result.InputTokens);
        Assert.Equal(120, result.OutputTokens);

        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal(HttpMethod.Post, handler.CapturedRequest!.Method);
        Assert.EndsWith("v1/messages", handler.CapturedRequest.RequestUri?.ToString());
        Assert.True(handler.CapturedRequest.Headers.TryGetValues("x-api-key", out var keys));
        Assert.Equal("sk-test-not-real", keys.Single());

        Assert.NotNull(handler.CapturedRequestBody);
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.Equal("claude-sonnet-4-6", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("user", doc.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Contains("diff --git a/README.md", doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        var system = doc.RootElement.GetProperty("system").GetString() ?? "";
        Assert.StartsWith(ReviewService.SystemPrompt.Trim(), system.Trim());
        Assert.Contains("Tenant review preferences:", system);
    }
}
