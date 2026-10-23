using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Core.Services;

/// <summary>Merges history and bookmark matches into ranked omnibox suggestions.
/// Ranking is a pure function so it can be tested without storage.</summary>
public sealed class OmniboxSuggestionService
{
    private readonly IHistoryRepository _history;
    private readonly IBookmarkRepository _bookmarks;

    public OmniboxSuggestionService(IHistoryRepository history, IBookmarkRepository bookmarks)
    {
        _history = history;
        _bookmarks = bookmarks;
    }

    public async Task<IReadOnlyList<OmniboxSuggestion>> GetSuggestionsAsync(string query, int limit = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<OmniboxSuggestion>();
        var historyTask = _history.SearchAsync(query, 20, ct);
        var bookmarkTask = _bookmarks.SearchAsync(query, 20, ct);
        await Task.WhenAll(historyTask, bookmarkTask).ConfigureAwait(false);
        return Rank(query, historyTask.Result, bookmarkTask.Result, limit);
    }

    public static IReadOnlyList<OmniboxSuggestion> Rank(
        string query,
        IEnumerable<HistoryEntry> history,
        IEnumerable<Bookmark> bookmarks,
        int limit)
    {
        var byUrl = new Dictionary<string, OmniboxSuggestion>(StringComparer.OrdinalIgnoreCase);

        foreach (var b in bookmarks)
        {
            byUrl[b.Url] = new OmniboxSuggestion
            {
                Title = b.Title,
                Url = b.Url,
                Kind = OmniboxSuggestionKind.Bookmark,
                VisitCount = 0
            };
        }

        foreach (var h in history)
        {
            if (byUrl.TryGetValue(h.Url, out var existing) && existing.Kind == OmniboxSuggestionKind.Bookmark)
            {
                byUrl[h.Url] = new OmniboxSuggestion
                {
                    Title = existing.Title,
                    Url = h.Url,
                    Kind = OmniboxSuggestionKind.Bookmark,
                    VisitCount = h.VisitCount
                };
            }
            else
            {
                byUrl[h.Url] = new OmniboxSuggestion
                {
                    Title = h.Title,
                    Url = h.Url,
                    Kind = OmniboxSuggestionKind.History,
                    VisitCount = h.VisitCount
                };
            }
        }

        double Score(OmniboxSuggestion s)
        {
            var score = 0d;
            if (s.Kind == OmniboxSuggestionKind.Bookmark) score += 40;
            if (s.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 30;
            else if (s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 15;
            if (s.Url.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 10;
            score += Math.Min(20, Math.Log10(s.VisitCount + 1) * 10);
            return score;
        }

        return byUrl.Values
            .OrderByDescending(Score)
            .ThenBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
    }
}
