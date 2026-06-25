using System.Text.Json;
using EngineIQ.AIEngine.Anthropic;
using EngineIQ.Domain.Persistence;

namespace EngineIQ.Tests.Unit;

public class AnthropicReviewResponseParserTests
{
    [Fact]
    public void TryParseAssistantText_extracts_text_blocks_from_messages_response()
    {
        const string json = """
            {
              "id": "msg_01",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "## Notes\n" },
                { "type": "text", "text": "- Looks good." }
              ],
              "usage": { "input_tokens": 100, "output_tokens": 50 }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        Assert.True(AnthropicReviewResponseParser.TryParseAssistantText(doc.RootElement, out var text));
        Assert.Equal("## Notes\n- Looks good.", text);
    }

    [Fact]
    public void TryParseUsage_reads_input_and_output_tokens()
    {
        const string json = """{ "usage": { "input_tokens": 1200, "output_tokens": 340 } }""";
        using var doc = JsonDocument.Parse(json);
        Assert.True(AnthropicReviewResponseParser.TryParseUsage(doc.RootElement, out var input, out var output));
        Assert.Equal(1200, input);
        Assert.Equal(340, output);
    }

    [Fact]
    public void ParseFindingsFromMarkdown_maps_bullet_lines_to_AI_findings()
    {
        var md = "- First issue\n- Second issue in `Program.cs:10`";
        var findings = AnthropicReviewResponseParser.ParseFindingsFromMarkdown(md);
        Assert.Equal(2, findings.Count);
        Assert.Equal(FindingSources.AI, findings[0].Source);
        Assert.Equal("Program.cs", findings[1].FilePath);
        Assert.Equal(10, findings[1].LineNumber);
    }

    [Fact]
    public void EstimateBulletFindingCount_counts_markdown_bullets()
    {
        var md = "## Notes\n- First\n- Second\n1. Third\nplain";
        Assert.Equal(3, AnthropicReviewResponseParser.EstimateBulletFindingCount(md));
    }

    [Fact]
    public void EstimateZarCost_uses_list_prices_and_fx()
    {
        // 1M in @ $3/M + 0.5M out @ $15/M = $3 + $7.5 = $10.5 → × 18.5 = R194.25
        var zar = AnthropicReviewResponseParser.EstimateZarCost(
            1_000_000,
            500_000,
            inputUsdPerMillion: 3,
            outputUsdPerMillion: 15,
            usdToZar: 18.5);
        Assert.Equal(194.25m, zar);
    }
}
