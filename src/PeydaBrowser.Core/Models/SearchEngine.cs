namespace PeydaBrowser.Core.Models;

/// <summary>
/// A search engine definition. <see cref="UrlTemplate"/> must contain the
/// <c>{query}</c> placeholder which is replaced with the URL-encoded query.
/// No private search API is involved: the browser simply navigates to the
/// public results page, so no API key is required.
/// </summary>
public sealed class SearchEngine
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string UrlTemplate { get; set; }

    /// <summary>Optional omnibox bang shortcut without the leading '!'. Example: "g" for "!g".</summary>
    public string? Bang { get; set; }

    /// <summary>Optional icon URL. Trading marks of search providers are not bundled with Peyda.</summary>
    public string? IconUrl { get; set; }

    public int SortOrder { get; set; }
    public bool IsBuiltIn { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }

    public static class BuiltIn
    {
        public const string GoogleId = "google";
        public const string BraveId = "brave";
        public const string DuckDuckGoId = "duckduckgo";

        public static IReadOnlyList<SearchEngine> CreateDefaults(DateTimeOffset nowUtc) => new[]
        {
            new SearchEngine { Id = GoogleId, Name = "Google", UrlTemplate = "https://www.google.com/search?q={query}", Bang = "g", IconUrl = "https://www.google.com/favicon.ico", SortOrder = 0, IsBuiltIn = true, CreatedUtc = nowUtc },
            new SearchEngine { Id = BraveId, Name = "Brave Search", UrlTemplate = "https://search.brave.com/search?q={query}", Bang = "b", IconUrl = "https://search.brave.com/favicon.ico", SortOrder = 1, IsBuiltIn = true, CreatedUtc = nowUtc },
            new SearchEngine { Id = DuckDuckGoId, Name = "DuckDuckGo", UrlTemplate = "https://duckduckgo.com/?q={query}", Bang = "d", IconUrl = "https://duckduckgo.com/favicon.ico", SortOrder = 2, IsBuiltIn = true, CreatedUtc = nowUtc }
        };
    }
}
