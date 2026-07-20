using EngineIQ.Domain.Search;

namespace EngineIQ.Tests.Unit;

public class CodeSearchQueryTokensTests
{
    [Fact]
    public void ExtractIdentifiers_finds_camel_case_symbol()
    {
        var tokens = CodeSearchQueryTokens.ExtractIdentifiers("InvoiceStatusHistory broken on save");
        Assert.Contains("InvoiceStatusHistory", tokens);
        Assert.Contains("broken", tokens); // length >= 4
        Assert.DoesNotContain("on", tokens);
    }

    [Fact]
    public void BuildIdentifierTsQuery_contains_identifier()
    {
        var q = CodeSearchQueryTokens.BuildIdentifierTsQuery("InvoiceStatusHistory broken on save");
        Assert.NotNull(q);
        Assert.Contains("InvoiceStatusHistory", q);
        Assert.Contains("|", q);
    }

    [Fact]
    public void Sanitize_and_build_do_not_throw_on_hostile_input()
    {
        const string hostile = "foo &|!():* bar_InvoiceService";
        var sanitized = CodeSearchQueryTokens.SanitizeTsQueryToken("&|!():*");
        Assert.Equal(string.Empty, sanitized);

        var q = CodeSearchQueryTokens.BuildIdentifierTsQuery(hostile);
        Assert.NotNull(q);
        Assert.DoesNotContain("&", q);
        Assert.DoesNotContain("|", q.Replace(" | ", ""));
        Assert.Contains("InvoiceService", q!);
    }
}
