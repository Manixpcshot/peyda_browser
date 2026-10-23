using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.App.ViewModels;

public sealed class HistoryGroupViewModel(string title, IEnumerable<HistoryEntry> entries)
{
    public string Title { get; } = title;
    public ObservableCollection<HistoryEntry> Entries { get; } = new(entries);
}

public sealed partial class HistoryViewModel : InternalPageViewModel
{
    private readonly HistoryService _history;
    private readonly IClock _clock;
    private CancellationTokenSource? _searchCts;

    public HistoryViewModel(LocalizedStrings strings, HistoryService history, IClock clock) : base(strings)
    {
        _history = history;
        _clock = clock;
    }

    public ObservableCollection<HistoryGroupViewModel> Groups { get; } = new();

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public override Task LoadAsync(string? argument, CancellationToken ct = default) => ReloadAsync(ct);

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var entries = string.IsNullOrWhiteSpace(SearchText)
            ? await _history.GetRecentAsync(300, ct).ConfigureAwait(true)
            : await _history.SearchAsync(SearchText.Trim(), 300, ct).ConfigureAwait(true);

        Groups.Clear();
        foreach (var group in HistoryGrouper.Group(entries, _clock.UtcNow, TimeZoneInfo.Local))
        {
            Groups.Add(new HistoryGroupViewModel(group.Bucket switch
            {
                HistoryBucket.Today => Strings.HistoryToday,
                HistoryBucket.Yesterday => Strings.HistoryYesterday,
                HistoryBucket.Last7Days => Strings.HistoryLast7days,
                _ => Strings.HistoryOlder
            }, group.Entries));
        }
        IsEmpty = Groups.Count == 0;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        _ = DelayedReloadAsync(cts.Token);
    }

    private async Task DelayedReloadAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct).ConfigureAwait(true);
            await ReloadAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void OpenEntry(HistoryEntry? entry)
    {
        if (entry is not null) NavigateHere(entry.Url);
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(HistoryEntry? entry)
    {
        if (entry is null) return;
        await _history.DeleteAsync(entry.Id).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    public async Task ClearAsync(ClearTimeRange range)
    {
        await _history.ClearAsync(range).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }
}

public sealed partial class DownloadsViewModel : InternalPageViewModel
{
    private readonly DownloadManager _manager;

    public DownloadsViewModel(LocalizedStrings strings, DownloadManager manager) : base(strings)
    {
        _manager = manager;
        _manager.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<DownloadItemViewModel> Items => _manager.Items;
    public bool IsEmpty => _manager.Items.Count == 0;

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        await _manager.ClearCompletedAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed partial class BookmarksViewModel : InternalPageViewModel
{
    private readonly IBookmarkRepository _repository;
    private readonly IClock _clock;

    public BookmarksViewModel(LocalizedStrings strings, IBookmarkRepository repository, IClock clock) : base(strings)
    {
        _repository = repository;
        _clock = clock;
    }

    public ObservableCollection<BookmarkFolder> Folders { get; } = new();
    public ObservableCollection<Bookmark> Items { get; } = new();

    /// <summary>"all" sentinel id used by the folder list UI.</summary>
    public const string AllBookmarksId = "all";

    [ObservableProperty] public partial string SelectedFolderId { get; set; } = AllBookmarksId;
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public override Task LoadAsync(string? argument, CancellationToken ct = default) => ReloadAsync(ct);

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        Folders.Clear();
        Folders.Add(new BookmarkFolder { Id = AllBookmarksId, Name = Strings.BookmarksAll, CreatedUtc = _clock.UtcNow });
        foreach (var folder in await _repository.GetFoldersAsync(ct).ConfigureAwait(true))
            Folders.Add(folder);

        IReadOnlyList<Bookmark> items;
        if (!string.IsNullOrWhiteSpace(SearchText))
            items = await _repository.SearchAsync(SearchText.Trim(), 500, ct).ConfigureAwait(true);
        else if (SelectedFolderId == AllBookmarksId)
            items = await _repository.GetAllAsync(ct).ConfigureAwait(true);
        else
            items = await _repository.GetByFolderAsync(SelectedFolderId, ct).ConfigureAwait(true);

        Items.Clear();
        foreach (var item in items) Items.Add(item);
        IsEmpty = Items.Count == 0;
        Tab?.Shell.NotifyBookmarksChanged();
    }

    partial void OnSelectedFolderIdChanged(string value) => _ = ReloadAsync();
    partial void OnSearchTextChanged(string value) => _ = ReloadAsync();

    [RelayCommand]
    private void OpenBookmark(Bookmark? bookmark)
    {
        if (bookmark is not null) NavigateHere(bookmark.Url);
    }

    [RelayCommand]
    private async Task DeleteBookmarkAsync(Bookmark? bookmark)
    {
        if (bookmark is null) return;
        await _repository.DeleteAsync(bookmark.Id).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    public async Task<BookmarkFolder> AddFolderAsync(string name, string? parentId)
    {
        var folder = new BookmarkFolder
        {
            Id = "folder-" + Guid.NewGuid().ToString("N")[..12],
            ParentId = parentId == AllBookmarksId ? null : parentId,
            Name = name.Trim(),
            CreatedUtc = _clock.UtcNow
        };
        await _repository.AddFolderAsync(folder).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        return folder;
    }

    public async Task DeleteFolderAsync(BookmarkFolder folder)
    {
        if (folder.Id == AllBookmarksId) return;
        await _repository.DeleteFolderAsync(folder.Id).ConfigureAwait(true);
        if (SelectedFolderId == folder.Id) SelectedFolderId = AllBookmarksId;
        await ReloadAsync().ConfigureAwait(true);
    }

    public async Task UpdateBookmarkAsync(Bookmark bookmark)
    {
        bookmark.ModifiedUtc = _clock.UtcNow;
        await _repository.UpdateAsync(bookmark).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Imports parsed HTML bookmarks, creating folders along each item's folder path.</summary>
    public async Task<int> ImportAsync(BookmarkImportResult import, CancellationToken ct = default)
    {
        var existingFolders = (await _repository.GetFoldersAsync(ct).ConfigureAwait(true)).ToList();
        var folderIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> EnsureFolderPathAsync(IReadOnlyList<string> path)
        {
            string? parentId = null;
            var built = string.Empty;
            foreach (var segment in path)
            {
                built = built.Length == 0 ? segment : built + "/" + segment;
                if (folderIdsByPath.TryGetValue(built, out var known))
                {
                    parentId = known;
                    continue;
                }
                var existing = existingFolders.FirstOrDefault(f =>
                    f.ParentId == parentId && string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    folderIdsByPath[built] = existing.Id;
                    parentId = existing.Id;
                    continue;
                }
                var folder = new BookmarkFolder
                {
                    Id = "folder-" + Guid.NewGuid().ToString("N")[..12],
                    ParentId = parentId,
                    Name = segment,
                    CreatedUtc = _clock.UtcNow
                };
                await _repository.AddFolderAsync(folder, ct).ConfigureAwait(true);
                existingFolders.Add(folder);
                folderIdsByPath[built] = folder.Id;
                parentId = folder.Id;
            }
            return parentId;
        }

        var added = 0;
        foreach (var item in import.Bookmarks)
        {
            var folderId = await EnsureFolderPathAsync(item.FolderPath).ConfigureAwait(true);
            var now = item.AddedUtc ?? _clock.UtcNow;
            await _repository.AddAsync(new Bookmark
            {
                Id = "bm-" + Guid.NewGuid().ToString("N")[..12],
                FolderId = folderId,
                Title = item.Title,
                Url = item.Url,
                CreatedUtc = now,
                ModifiedUtc = now
            }, ct).ConfigureAwait(true);
            added++;
        }
        await ReloadAsync(ct).ConfigureAwait(true);
        return added;
    }
}
