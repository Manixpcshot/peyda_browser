using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Core.Interfaces;

/// <summary>Time source, abstracted for deterministic tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public interface ISearchEngineRepository
{
    Task<IReadOnlyList<SearchEngine>> GetAllAsync(CancellationToken ct = default);
    Task<SearchEngine?> GetByIdAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(SearchEngine engine, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SetSortOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default);
}

public interface IHistoryRepository
{
    Task UpsertVisitAsync(string url, string title, DateTimeOffset visitedUtc, string? faviconUrl = null, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit, CancellationToken ct = default);
    Task<IReadOnlyList<HistoryEntry>> SearchAsync(string query, int limit, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task DeleteRangeAsync(DateTimeOffset? fromUtc, CancellationToken ct = default);
    Task DeleteByUrlAsync(string url, CancellationToken ct = default);
}

public interface IBookmarkRepository
{
    Task<IReadOnlyList<Bookmark>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Bookmark>> GetByFolderAsync(string? folderId, CancellationToken ct = default);
    Task<IReadOnlyList<Bookmark>> SearchAsync(string query, int limit, CancellationToken ct = default);
    Task<Bookmark?> FindByUrlAsync(string url, CancellationToken ct = default);

    /// <returns>The bookmark, or the pre-existing one when the URL is already bookmarked in the same folder.</returns>
    Task<Bookmark> AddAsync(Bookmark bookmark, CancellationToken ct = default);
    Task UpdateAsync(Bookmark bookmark, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<BookmarkFolder>> GetFoldersAsync(CancellationToken ct = default);
    Task AddFolderAsync(BookmarkFolder folder, CancellationToken ct = default);
    Task UpdateFolderAsync(BookmarkFolder folder, CancellationToken ct = default);
    Task DeleteFolderAsync(string id, CancellationToken ct = default);
}

public interface IDownloadRepository
{
    Task<IReadOnlyList<DownloadRecord>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(DownloadRecord record, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task ClearCompletedAsync(CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task SaveAutoSessionAsync(SessionSnapshot snapshot, CancellationToken ct = default);
    Task<SessionSnapshot?> GetAutoSessionAsync(CancellationToken ct = default);
    Task ClearAutoSessionAsync(CancellationToken ct = default);
    Task PushClosedTabAsync(ClosedTab tab, int maxItems, CancellationToken ct = default);
    Task<ClosedTab?> PopClosedTabAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ClosedTab>> GetClosedTabsAsync(CancellationToken ct = default);
}

public interface IProfileRepository
{
    Task<BrowserProfile?> GetAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(BrowserProfile profile, CancellationToken ct = default);
}

public interface IThemeRepository
{
    Task<IReadOnlyList<(string Id, string Name, string Json, bool IsBuiltIn)>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(string id, string name, string json, bool isBuiltIn, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public interface IShortcutRepository
{
    Task<IReadOnlyList<NewTabShortcut>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(NewTabShortcut shortcut, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SetSortOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default);
}

public interface IPermissionRepository
{
    Task<SitePermission?> GetAsync(string origin, BrowserPermissionKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<SitePermission>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(SitePermission permission, CancellationToken ct = default);
    Task DeleteAsync(string origin, BrowserPermissionKind kind, CancellationToken ct = default);
    Task DeleteAllForOriginAsync(string origin, CancellationToken ct = default);
}
