using NSubstitute;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.UnitTests;

public class SearchUrlBuilderTests
{
    private static SearchEngine Google => new()
    {
        Id = "google", Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}",
        CreatedUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public void Query_IsUrlEncoded()
    {
        var url = SearchUrlBuilder.BuildSearchUrl(Google, "cats & dogs?");
        Assert.Equal("https://www.google.com/search?q=cats%20%26%20dogs%3F", url);
    }

    [Fact]
    public void PersianQuery_IsUtf8PercentEncoded()
    {
        var url = SearchUrlBuilder.BuildSearchUrl(Google, "تست");
        Assert.Equal("https://www.google.com/search?q=%D8%AA%D8%B3%D8%AA", url);
    }

    [Fact]
    public void MissingPlaceholder_Throws()
    {
        var bad = new SearchEngine { Id = "x", Name = "X", UrlTemplate = "https://x.test/search", CreatedUtc = DateTimeOffset.UtcNow };
        Assert.Throws<InvalidOperationException>(() => SearchUrlBuilder.BuildSearchUrl(bad, "q"));
    }

    [Theory]
    [InlineData(null, "error.template.empty")]
    [InlineData("https://x.test/search", "error.template.missingQuery")]
    [InlineData("javascript:alert({query})", "error.template.invalidScheme")]
    [InlineData("not a url {query}", "error.template.invalidUrl")]
    public void InvalidTemplates_AreRejected(string? template, string expectedError)
    {
        Assert.Equal(expectedError, SearchUrlBuilder.ValidateTemplate(template));
    }

    [Theory]
    [InlineData("https://www.google.com/search?q={query}")]
    [InlineData("https://search.brave.com/search?q={query}")]
    [InlineData("https://duckduckgo.com/?q={query}")]
    [InlineData("http://localhost:8080/?q={query}")]
    public void ValidTemplates_AreAccepted(string template)
    {
        Assert.Null(SearchUrlBuilder.ValidateTemplate(template));
    }

    [Fact]
    public void BuiltInEngines_MatchSpecification()
    {
        var defaults = SearchEngine.BuiltIn.CreateDefaults(DateTimeOffset.UtcNow);
        Assert.Equal(3, defaults.Count);
        Assert.Equal("https://www.google.com/search?q={query}", defaults.Single(e => e.Id == "google").UrlTemplate);
        Assert.Equal("https://search.brave.com/search?q={query}", defaults.Single(e => e.Id == "brave").UrlTemplate);
        Assert.Equal("https://duckduckgo.com/?q={query}", defaults.Single(e => e.Id == "duckduckgo").UrlTemplate);
        Assert.Equal(new[] { "g", "b", "d" }, defaults.Select(e => e.Bang).ToArray());
    }
}

internal sealed class InMemorySearchEngineRepository : ISearchEngineRepository
{
    public List<SearchEngine> Items { get; } = new();
    public Task<IReadOnlyList<SearchEngine>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SearchEngine>>(Items.OrderBy(e => e.SortOrder).ToList());
    public Task<SearchEngine?> GetByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(e => e.Id == id));
    public Task UpsertAsync(SearchEngine engine, CancellationToken ct = default)
    {
        Items.RemoveAll(e => e.Id == engine.Id);
        Items.Add(engine);
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        Items.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
    public Task SetSortOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default)
    {
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var engine = Items.FirstOrDefault(e => e.Id == orderedIds[i]);
            if (engine is not null) engine.SortOrder = i;
        }
        return Task.CompletedTask;
    }
}

public class SearchEngineServiceTests
{
    [Fact]
    public async Task EnsureBuiltIns_SeedsThreeEnginesOnce()
    {
        var repo = new InMemorySearchEngineRepository();
        var clock = Substitute.For<IClock>();
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        var service = new SearchEngineService(repo, clock);

        await service.EnsureBuiltInsAsync();
        await service.EnsureBuiltInsAsync();

        Assert.Equal(3, repo.Items.Count);
        Assert.All(repo.Items, e => Assert.Equal(now, e.CreatedUtc));
    }

    [Fact]
    public async Task Save_DuplicateBang_IsRejected()
    {
        var repo = new InMemorySearchEngineRepository();
        var service = new SearchEngineService(repo, new SystemClock());
        await service.EnsureBuiltInsAsync();

        var error = await service.SaveAsync(new SearchEngine
        {
            Id = "custom", Name = "Custom", UrlTemplate = "https://s.test/?q={query}", Bang = "g",
            CreatedUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal("error.engine.bangDuplicate", error);
    }

    [Fact]
    public async Task Delete_LastEngine_IsForbidden()
    {
        var repo = new InMemorySearchEngineRepository();
        var service = new SearchEngineService(repo, new SystemClock());
        repo.Items.Add(new SearchEngine { Id = "only", Name = "Only", UrlTemplate = "https://s.test/?q={query}", CreatedUtc = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("only"));
    }

    [Fact]
    public async Task Resolve_FallsBackToDefaultThenFirst()
    {
        var repo = new InMemorySearchEngineRepository();
        var service = new SearchEngineService(repo, new SystemClock());
        await service.EnsureBuiltInsAsync();

        var resolved = await service.ResolveAsync("does-not-exist", SearchEngine.BuiltIn.BraveId);
        Assert.Equal("brave", resolved.Id);
    }
}
