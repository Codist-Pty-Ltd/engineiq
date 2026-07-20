using System.Net;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Indexing;
using EngineIQ.Infrastructure.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EngineIQ.Tests.Unit;

public class VoyageEmbeddingClientTests
{
    [Fact]
    public async Task EmbedAsync_splits_batches_at_BatchSize()
    {
        var handler = new RecordingHandler(_ => OkEmbeddings(2));
        var client = CreateClient(handler, batchSize: 2, maxInputChars: 24000);

        var inputs = new[] { "a", "b", "c", "d" };
        var vectors = await client.EmbedAsync(inputs, EmbeddingInputType.Document);

        Assert.Equal(4, vectors.Count);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal(2, CountInputs(handler.RequestBodies[0]));
        Assert.Equal(2, CountInputs(handler.RequestBodies[1]));
    }

    [Fact]
    public async Task EmbedAsync_truncates_inputs_over_MaxInputChars()
    {
        var handler = new RecordingHandler(_ => OkEmbeddings(1));
        var client = CreateClient(handler, batchSize: 96, maxInputChars: 10);

        await client.EmbedAsync(new[] { new string('x', 50) }, EmbeddingInputType.Document);

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var input = doc.RootElement.GetProperty("input")[0].GetString()!;
        Assert.Equal(10, input.Length);
        Assert.Equal(1024, doc.RootElement.GetProperty("output_dimension").GetInt32());
    }

    [Fact]
    public async Task EmbedAsync_retries_on_429_then_succeeds()
    {
        var attempts = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("rate") };
            return OkEmbeddings(1);
        });
        var client = CreateClient(handler, batchSize: 96, maxInputChars: 24000);

        var vectors = await client.EmbedAsync(new[] { "hello" }, EmbeddingInputType.Query);
        Assert.Single(vectors);
        Assert.Equal(2, attempts);
        Assert.Equal("query", JsonDocument.Parse(handler.RequestBodies[^1]).RootElement.GetProperty("input_type").GetString());
    }

    private static VoyageEmbeddingClient CreateClient(HttpMessageHandler handler, int batchSize, int maxInputChars)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.voyageai.com/") };
        var options = Options.Create(new VoyageOptions
        {
            ApiKey = "pa-test",
            Model = "voyage-code-3",
            Dimensions = 1024,
            BatchSize = batchSize,
            MaxInputChars = maxInputChars,
        });
        return new VoyageEmbeddingClient(http, options, NullLogger<VoyageEmbeddingClient>.Instance);
    }

    private static HttpResponseMessage OkEmbeddings(int count)
    {
        var data = Enumerable.Range(0, count)
            .Select(i => new { embedding = new float[] { 0.1f, 0.2f }, index = i });
        var body = JsonSerializer.Serialize(new { data, usage = new { total_tokens = count * 3 } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static int CountInputs(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("input").GetArrayLength();

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            return _responder(request);
        }
    }
}
