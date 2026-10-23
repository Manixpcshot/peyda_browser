using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.UnitTests;

public class AddressBarParserTests
{
    private readonly AddressBarParser _parser = new();
    private static readonly Dictionary<string, string> Bangs = new()
    {
        ["g"] = SearchEngine.BuiltIn.GoogleId,
        ["b"] = SearchEngine.BuiltIn.BraveId,
        ["d"] = SearchEngine.BuiltIn.DuckDuckGoId
    };

    [Theory]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://example.com")]
    public void FullHttpUrls_Navigate(string input)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.Url, result.Kind);
        Assert.StartsWith("http", result.Target);
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("www.example.com/docs", "https://www.example.com/docs")]
    [InlineData("sub.domain.co.uk", "https://sub.domain.co.uk")]
    public void DomainLikeInput_GetsHttps(string input, string expectedPrefix)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.Url, result.Kind);
        Assert.StartsWith(expectedPrefix, result.Target);
    }

    [Theory]
    [InlineData("localhost:8080/app")]
    [InlineData("127.0.0.1:5000")]
    [InlineData("[::1]:9000/x")]
    public void LocalAddresses_NavigateWithHttp(string input)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.Url, result.Kind);
        Assert.StartsWith("http://", result.Target);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("how to cook rice")]
    [InlineData("تاریخچه ایران")]
    [InlineData("news")]
    [InlineData("3.14")]
    [InlineData("example.com:99999")]
    [InlineData("not a url at all")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    public void NonUrlInput_BecomesSearch(string input)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.Search, result.Kind);
        Assert.Equal(input, result.Query);
    }

    [Fact]
    public void PersianQuery_IsSearch_AndPreserved()
    {
        var result = _parser.Parse("مرورگر پیدا", Bangs);
        Assert.Equal(AddressBarInputKind.Search, result.Kind);
        Assert.Equal("مرورگر پیدا", result.Query);
    }

    [Fact]
    public void IdnDomain_BecomesPunycodeUrl()
    {
        var result = _parser.Parse("مثال.ایران", Bangs);
        Assert.Equal(AddressBarInputKind.Url, result.Kind);
        Assert.StartsWith("https://xn--", result.Target);
        Assert.DoesNotContain("مثال", result.Target);
    }

    [Theory]
    [InlineData("!g cats", "google", "cats")]
    [InlineData("!b privacy browser", "brave", "privacy browser")]
    [InlineData("!d تست جستجو", "duckduckgo", "تست جستجو")]
    public void BangSearch_SelectsEngine(string input, string expectedEngine, string expectedQuery)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.Search, result.Kind);
        Assert.Equal(expectedEngine, result.SearchEngineId);
        Assert.Equal(expectedQuery, result.Query);
    }

    [Fact]
    public void UnknownBang_IsPlainSearch()
    {
        var result = _parser.Parse("!x hello", Bangs);
        Assert.Equal(AddressBarInputKind.Search, result.Kind);
        Assert.Null(result.SearchEngineId);
        Assert.Equal("!x hello", result.Query);
    }

    [Theory]
    [InlineData("peyda://settings", "settings")]
    [InlineData("peyda://newtab", "newtab")]
    [InlineData("peyda://history/", "history")]
    [InlineData("PEYDA://Downloads", "downloads")]
    public void InternalUrls_AreInternalPages(string input, string expectedPage)
    {
        var result = _parser.Parse(input, Bangs);
        Assert.Equal(AddressBarInputKind.InternalPage, result.Kind);
        Assert.Equal(expectedPage, result.InternalPage);
    }

    [Fact]
    public void EmptyInput_IsEmptySearch()
    {
        var result = _parser.Parse("   ", Bangs);
        Assert.Equal(AddressBarInputKind.Search, result.Kind);
        Assert.Equal(string.Empty, result.Query);
    }
}
