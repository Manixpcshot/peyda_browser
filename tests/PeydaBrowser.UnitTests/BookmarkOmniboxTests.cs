using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.UnitTests;

public class BookmarkHtmlTests
{
    [Fact]
    public void ExportThenImport_PreservesFoldersAndLinks()
    {
        var now = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        var folders = new List<BookmarkFolder>
        {
            new() { Id = "f1", Name = "Work", CreatedUtc = now },
            new() { Id = "f2", ParentId = "f1", Name = "Docs", CreatedUtc = now }
        };
        var bookmarks = new List<Bookmark>
        {
            new() { Id = "b1", FolderId = "f2", Title = "Spec & Notes", Url = "https://example.com/spec?a=1&b=2", CreatedUtc = now, ModifiedUtc = now },
            new() { Id = "b2", FolderId = null, Title = "Root link", Url = "https://www.wikipedia.org", CreatedUtc = now, ModifiedUtc = now }
        };

        var html = BookmarkHtmlService.Export(bookmarks, folders);
        var imported = BookmarkHtmlService.Import(html);

        Assert.Equal(2, imported.Bookmarks.Count);
        var spec = imported.Bookmarks.Single(b => b.Url.Contains("example.com"));
        Assert.Equal(new[] { "Work", "Docs" }, spec.FolderPath);
        Assert.Equal("Spec & Notes", spec.Title);
        Assert.Equal("https://example.com/spec?a=1&b=2", spec.Url);
        var root = imported.Bookmarks.Single(b => b.Url.Contains("wikipedia"));
        Assert.Empty(root.FolderPath);
    }

    [Fact]
    public void Import_SkipsDangerousSchemes()
    {
        const string html = """
            <DL><p>
              <DT><A HREF="javascript:alert(1)">evil</A>
              <DT><A HREF="data:text/html,<b>x</b>">data</A>
              <DT><A HREF="https://good.example">good</A>
            </DL><p>
            """;
        var result = BookmarkHtmlService.Import(html);
        Assert.Single(result.Bookmarks);
        Assert.Equal(2, result.SkippedCount);
    }
}

public class OmniboxRankingTests
{
    [Fact]
    public void Bookmarks_RankAbovePlainHistory_AndDuplicatesMerge()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new[]
        {
            new HistoryEntry { Url = "https://github.com", Title = "GitHub", VisitCount = 100, FirstVisitUtc = now, LastVisitUtc = now },
            new HistoryEntry { Url = "https://gitlab.com", Title = "GitLab", VisitCount = 3, FirstVisitUtc = now, LastVisitUtc = now }
        };
        var bookmarks = new[]
        {
            new Bookmark { Id = "b1", Title = "GitHub (saved)", Url = "https://github.com", CreatedUtc = now, ModifiedUtc = now }
        };

        var suggestions = OmniboxSuggestionService.Rank("git", history, bookmarks, limit: 8);

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("https://github.com", suggestions[0].Url);
        Assert.Equal(OmniboxSuggestionKind.Bookmark, suggestions[0].Kind);
        Assert.Equal(100, suggestions[0].VisitCount);
    }

    [Fact]
    public void Rank_RespectsLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var history = Enumerable.Range(0, 20)
            .Select(i => new HistoryEntry { Url = $"https://site{i}.example", Title = "site", VisitCount = i, FirstVisitUtc = now, LastVisitUtc = now })
            .ToList();
        var suggestions = OmniboxSuggestionService.Rank("site", history, Array.Empty<Bookmark>(), limit: 5);
        Assert.Equal(5, suggestions.Count);
    }
}
