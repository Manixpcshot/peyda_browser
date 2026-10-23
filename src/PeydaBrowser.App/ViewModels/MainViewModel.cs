using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using PeydaBrowser.App.Pages;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Core.Shortcuts;
using PeydaBrowser.Infrastructure.Profiles;
using PeydaBrowser.WebEngine;

namespace PeydaBrowser.App.ViewModels;

public sealed record BookmarkBarItem(string Title, string? Url, string? BookmarkId, IReadOnlyList<BookmarkBarItem> Children)
{
    public bool IsFolder => Url is null;
}

/// <summary>
/// Shell of one browser window: tab collection, session restore, bookmark bar,
/// WebView2 environment lifetime and the <see cref="IBrowserShell"/> contract
/// used by tabs and internal pages.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IBrowserShell
{
    private readonly ProfileContext _context;
    private readonly IServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly SessionService _sessionService;
    private readonly SearchEngineService _searchEngineService;
    private readonly IBookmarkRepository _bookmarks;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SemaphoreSlim _sessionSaveLock = new(1, 1);
    private CancellationTokenSource? _sessionDebounce;
    private Task<CoreWebView2Environment>? _environmentTask;

    public MainViewModel(
        ProfileContext context,
        IServiceProvider services,
        SettingsService settingsService,
        HistoryService historyService,
        SessionService sessionService,
        SearchEngineService searchEngineService,
        IBookmarkRepository bookmarks,
        ILocalizationService localization,
        LocalizedStrings strings,
        DownloadManager downloads,
        ThemeService themeService,
        ILogger<MainViewModel> logger)
    {
        _context = context;
        _services = services;
        _settingsService = settingsService;
        _historyService = historyService;
        _sessionService = sessionService;
        _searchEngineService = searchEngineService;
        _bookmarks = bookmarks;
        Localization = localization;
        Strings = strings;
        Downloads = downloads;
        ThemeService = themeService;
        _logger = logger;
    }

    // ---- Window integration (assigned by MainWindow) ----
    public Func<IntPtr>? WindowHandleProvider { get; set; }
    public Func<XamlRoot?>? XamlRootProvider { get; set; }
    public Action<bool>? SetFullscreenAction { get; set; }
    public Action? OpenPrivateWindowAction { get; set; }
    public Action? CloseWindowAction { get; set; }

    public event EventHandler? SettingsChanged;

    public ProfileContext Context => _context;
    public IServiceProvider Services => _services;
    public ThemeService ThemeService { get; }

    public BrowserSettings Settings { get; private set; } = new();
    public bool IsPrivateWindow => _context.IsPrivate;
    public ILocalizationService Localization { get; }
    public LocalizedStrings Strings { get; }
    public DownloadManager Downloads { get; }

    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new();
    public ObservableCollection<BookmarkBarItem> BookmarkBarItems { get; } = new();
    public ObservableCollection<SearchEngine> SearchEngines { get; } = new();

    [ObservableProperty] public partial BrowserTabViewModel? SelectedTab { get; set; }
    [ObservableProperty] public partial bool IsFullscreen { get; set; }

    // ============================================================ initialization

    public async Task LoadSettingsAsync(CancellationToken ct = default)
    {
        Settings = await _settingsService.LoadAsync(ct).ConfigureAwait(true);
        Localization.SetLanguage(Settings.Language);
        await _searchEngineService.EnsureBuiltInsAsync(ct).ConfigureAwait(true);
        await RefreshSearchEnginesAsync(ct).ConfigureAwait(true);
        await RefreshBookmarkBarAsync(ct).ConfigureAwait(true);
        await Downloads.LoadHistoryAsync(ct).ConfigureAwait(true);
        Downloads.ConfirmOpenDangerousAsync = ConfirmOpenDangerousAsync;
    }

    public async Task InitializeTabsAsync(string? initialUrl, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            OpenNewTab(initialUrl, activate: true);
            return;
        }

        if (IsPrivateWindow)
        {
            var tab = CreateTab();
            tab.ShowInternalPage(new InternalRoute(InternalPage.PrivateHome, null));
            Tabs.Add(tab);
            SelectedTab = tab;
            return;
        }

        switch (Settings.StartupBehavior)
        {
            case StartupBehavior.OpenHomePage:
                OpenNewTab(Settings.HomePage, activate: true);
                break;
            case StartupBehavior.OpenNewTabPage:
                OpenNewTab(null, activate: true);
                break;
            default: // RestorePreviousSession
                var session = await _sessionService.LoadAsync(ct).ConfigureAwait(true);
                if (session is null || session.Tabs.Count == 0)
                {
                    OpenNewTab(null, activate: true);
                }
                else
                {
                    BrowserTabViewModel? active = null;
                    foreach (var saved in session.Tabs)
                    {
                        var tab = CreateTab();
                        Tabs.Add(tab);
                        tab.IsPinned = saved.IsPinned;
                        tab.NavigateToUrl(saved.Url);
                        if (saved.IsMuted) tab.IsMuted = true;
                        if (saved.IsActive) active = tab;
                    }
                    SelectedTab = active ?? Tabs[^1];
                }
                break;
        }
    }

    // ============================================================ tab operations

    private BrowserTabViewModel CreateTab() =>
        ActivatorUtilities.CreateInstance<BrowserTabViewModel>(_services, (IBrowserShell)this);

    public void OpenNewTab(string? url = null, bool activate = true)
    {
        var tab = CreateTab();
        Tabs.Add(tab);
        if (string.IsNullOrWhiteSpace(url))
            tab.ShowInternalPage(new InternalRoute(
                IsPrivateWindow && Tabs.Count == 1 ? InternalPage.PrivateHome : InternalPage.NewTab, null));
        else
            tab.NavigateToUrl(url);
        if (activate) SelectedTab = tab;
        NotifySessionDirty();
    }

    public void OpenInternalPageInNewTab(InternalPage page) =>
        OpenNewTab(InternalPageRouter.ToUrl(page), activate: true);

    public void OpenInternalPageSmart(InternalPage page)
    {
        // Reuse the current tab when it already shows an internal page, so
        // shortcuts like Ctrl+H do not spawn endless tabs.
        if (SelectedTab?.CurrentInternalPage is not null)
            SelectedTab.NavigateToUrl(InternalPageRouter.ToUrl(page));
        else
            OpenInternalPageInNewTab(page);
    }

    public async Task CloseTabAsync(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.Remove(tab);
        tab.View?.CloseWebView();

        if (!IsPrivateWindow)
        {
            try
            {
                await _sessionService.PushClosedTabAsync(tab.ToSessionTab(isActive: false), isPrivateContext: false).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record closed tab");
            }
        }

        if (Tabs.Count == 0)
        {
            OpenNewTab(null, activate: true);
        }
        else if (SelectedTab == tab)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        NotifySessionDirty();
    }

    public async Task ReopenClosedTabAsync()
    {
        if (IsPrivateWindow) return;
        var closed = await _sessionService.PopClosedTabAsync().ConfigureAwait(true);
        if (closed is null) return;
        var tab = CreateTab();
        Tabs.Add(tab);
        tab.IsPinned = closed.IsPinned;
        tab.NavigateToUrl(closed.Url);
        if (closed.IsMuted) tab.IsMuted = true;
        SelectedTab = tab;
    }

    public void DuplicateTab(BrowserTabViewModel tab) => OpenNewTab(tab.CurrentUrl, activate: true);

    public async Task CloseOtherTabsAsync(BrowserTabViewModel keep)
    {
        foreach (var tab in Tabs.Where(t => t != keep).ToList())
            await CloseTabAsync(tab).ConfigureAwait(true);
    }

    public async Task CloseTabsToRightAsync(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        foreach (var other in Tabs.Skip(index + 1).ToList())
            await CloseTabAsync(other).ConfigureAwait(true);
    }

    public void SelectNextTab()
    {
        if (Tabs.Count == 0 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) + 1) % Tabs.Count];
    }

    public void SelectPreviousTab()
    {
        if (Tabs.Count == 0 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) - 1 + Tabs.Count) % Tabs.Count];
    }

    public void SelectTabByIndex(int index)
    {
        if (index >= 0 && index < Tabs.Count) SelectedTab = Tabs[index];
    }

    public void SelectLastTab()
    {
        if (Tabs.Count > 0) SelectedTab = Tabs[^1];
    }

    // ============================================================ shortcuts

    public async Task ExecuteShortcutAsync(BrowserCommand command, int argument)
    {
        var tab = SelectedTab;
        switch (command)
        {
            case BrowserCommand.NewTab:
                OpenNewTab(null, activate: true);
                tab = SelectedTab;
                tab?.RequestFocusAddressBar();
                break;
            case BrowserCommand.CloseTab:
                if (tab is not null) await CloseTabAsync(tab).ConfigureAwait(true);
                break;
            case BrowserCommand.ReopenClosedTab:
                await ReopenClosedTabAsync().ConfigureAwait(true);
                break;
            case BrowserCommand.FocusAddressBar:
                tab?.RequestFocusAddressBar();
                break;
            case BrowserCommand.Reload:
                tab?.DoReload(ignoreCache: false);
                break;
            case BrowserCommand.HardReload:
                tab?.DoReload(ignoreCache: true);
                break;
            case BrowserCommand.Back:
                tab?.View?.GoBack();
                break;
            case BrowserCommand.Forward:
                tab?.View?.GoForward();
                break;
            case BrowserCommand.AddBookmark:
                if (tab is not null && XamlRootProvider?.Invoke() is { } root)
                    await ToggleBookmarkAsync(tab, root).ConfigureAwait(true);
                break;
            case BrowserCommand.OpenHistory:
                OpenInternalPageSmart(InternalPage.History);
                break;
            case BrowserCommand.OpenDownloads:
                OpenInternalPageSmart(InternalPage.Downloads);
                break;
            case BrowserCommand.NewPrivateWindow:
                OpenPrivateWindow();
                break;
            case BrowserCommand.NextTab:
                SelectNextTab();
                break;
            case BrowserCommand.PreviousTab:
                SelectPreviousTab();
                break;
            case BrowserCommand.SelectTab:
                SelectTabByIndex(argument);
                break;
            case BrowserCommand.SelectLastTab:
                SelectLastTab();
                break;
            case BrowserCommand.ToggleFullscreen:
                SetFullscreen(!IsFullscreen);
                break;
            case BrowserCommand.OpenDevTools:
                if (Settings.DevToolsEnabled) tab?.View?.OpenDevTools();
                break;
            case BrowserCommand.ZoomIn:
                tab?.ZoomIn();
                break;
            case BrowserCommand.ZoomOut:
                tab?.ZoomOut();
                break;
            case BrowserCommand.ZoomReset:
                tab?.ZoomReset();
                break;
            case BrowserCommand.Print:
                tab?.View?.Print();
                break;
        }
    }

    // ============================================================ IBrowserShell

    public async Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
    {
        _environmentTask ??= CreateEnvironmentAsync();
        try
        {
            return await _environmentTask.ConfigureAwait(true);
        }
        catch
        {
            _environmentTask = null;
            throw;
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(_context.WebView2UserDataDirectory);
        _logger.LogInformation("Creating WebView2 environment for profile {ProfileId}", _context.Profile.Id);
        // Positional on purpose: parameter names differ between the WebView2
        // .NET projections (browserExecutableFolder, userDataFolder, options).
        return await CoreWebView2Environment.CreateAsync(
            null,
            _context.WebView2UserDataDirectory,
            new CoreWebView2EnvironmentOptions());
    }

    public void InvalidateWebViewEnvironment() => _environmentTask = null;

    public void OpenPrivateWindow() => OpenPrivateWindowAction?.Invoke();

    public void SetFullscreen(bool fullscreen)
    {
        if (IsFullscreen == fullscreen) return;
        IsFullscreen = fullscreen;
        SetFullscreenAction?.Invoke(fullscreen);
    }

    public async Task<string?> PickDownloadPathAsync(string suggestedFileName)
    {
        var hwnd = WindowHandleProvider?.Invoke() ?? IntPtr.Zero;
        var ext = Path.GetExtension(suggestedFileName);
        if (string.IsNullOrEmpty(ext)) ext = ".dat";
        return await PickerHelper.PickSaveFileAsync(hwnd, suggestedFileName, "File", ext).ConfigureAwait(true);
    }

    public async Task<bool> ConfirmExternalProtocolAsync(string url, XamlRoot xamlRoot)
    {
        var scheme = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Scheme : "?";
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Strings.ExternalTitle,
            Content = Strings.Format(LocKeys.ExternalMessage, scheme, url),
            PrimaryButtonText = Strings.ExternalOpen,
            CloseButtonText = Strings.CommonCancel,
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmOpenDangerousAsync(DownloadItemViewModel item)
    {
        if (XamlRootProvider?.Invoke() is not { } root) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Strings.DownloadsDangerousTitle,
            Content = Strings.Format(LocKeys.DownloadsDangerousMessage, item.FileName),
            PrimaryButtonText = Strings.DownloadsOpenFile,
            CloseButtonText = Strings.CommonCancel,
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task RecordHistoryAsync(string url, string title, string? faviconUrl)
    {
        try
        {
            await _historyService.RecordVisitAsync(url, title, Settings.RecordHistory, IsPrivateWindow, faviconUrl).ConfigureAwait(true);
            if (SelectedTab?.CurrentUrl == url)
                SelectedTab.IsBookmarked = await IsBookmarkedAsync(url).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record history visit");
        }
    }

    public async Task<bool> IsBookmarkedAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || PeydaUrl.IsPeydaUrl(url)) return false;
        return await _bookmarks.FindByUrlAsync(url).ConfigureAwait(true) is not null;
    }

    public async Task ToggleBookmarkAsync(BrowserTabViewModel tab, XamlRoot xamlRoot)
    {
        var url = tab.CurrentUrl;
        if (PeydaUrl.IsPeydaUrl(url) || string.IsNullOrWhiteSpace(url)) return;

        var existing = await _bookmarks.FindByUrlAsync(url).ConfigureAwait(true);
        if (existing is not null)
        {
            await _bookmarks.DeleteAsync(existing.Id).ConfigureAwait(true);
            tab.IsBookmarked = false;
            await RefreshBookmarkBarAsync().ConfigureAwait(true);
            return;
        }

        var folders = await _bookmarks.GetFoldersAsync().ConfigureAwait(true);
        var dialog = new BookmarkEditDialog(Strings, tab.DisplayTitle, url, folders)
        {
            XamlRoot = xamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var now = DateTimeOffset.UtcNow;
        await _bookmarks.AddAsync(new Bookmark
        {
            Id = "bm-" + Guid.NewGuid().ToString("N")[..12],
            FolderId = dialog.SelectedFolderId,
            Title = string.IsNullOrWhiteSpace(dialog.BookmarkTitle) ? dialog.BookmarkUrl : dialog.BookmarkTitle.Trim(),
            Url = dialog.BookmarkUrl.Trim(),
            CreatedUtc = now,
            ModifiedUtc = now
        }).ConfigureAwait(true);

        tab.IsBookmarked = true;
        await RefreshBookmarkBarAsync().ConfigureAwait(true);
    }

    public async Task ClearBrowsingDataAsync(ClearTimeRange range, bool includeHistory, bool includeSiteData)
    {
        if (includeHistory)
            await _historyService.ClearAsync(range).ConfigureAwait(true);
        if (includeSiteData && SelectedTab?.View is not null)
            await SelectedTab.View.ClearSiteDataAsync(range).ConfigureAwait(true);
    }

    public void NotifyBookmarksChanged() => _ = RefreshBookmarkBarAsync();

    public async Task RefreshBookmarkBarAsync(CancellationToken ct = default)
    {
        try
        {
            var bookmarks = await _bookmarks.GetAllAsync(ct).ConfigureAwait(true);
            var folders = await _bookmarks.GetFoldersAsync(ct).ConfigureAwait(true);
            BookmarkBarItems.Clear();

            List<BookmarkBarItem> ChildrenOf(string? folderId) =>
                bookmarks.Where(b => b.FolderId == folderId)
                    .OrderBy(b => b.SortOrder).ThenBy(b => b.Title)
                    .Select(b => new BookmarkBarItem(b.Title, b.Url, b.Id, Array.Empty<BookmarkBarItem>()))
                    .ToList();

            foreach (var folder in folders.Where(f => f.ParentId is null).OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
                BookmarkBarItems.Add(new BookmarkBarItem(folder.Name, null, folder.Id, ChildrenOf(folder.Id)));
            foreach (var rootBookmark in ChildrenOf(null))
                BookmarkBarItems.Add(rootBookmark);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh bookmark bar");
        }
    }

    public async Task RefreshSearchEnginesAsync(CancellationToken ct = default)
    {
        var engines = await _searchEngineService.GetAllAsync(ct).ConfigureAwait(true);
        SearchEngines.Clear();
        foreach (var engine in engines) SearchEngines.Add(engine);
    }

    // ============================================================ settings & session

    public async Task ApplySettingsAsync(BrowserSettings updated, CancellationToken ct = default)
    {
        Settings = updated;
        await _settingsService.SaveAsync(updated, ct).ConfigureAwait(true);
        Localization.SetLanguage(updated.Language);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifySessionDirty()
    {
        if (IsPrivateWindow) return;
        _sessionDebounce?.Cancel();
        var cts = _sessionDebounce = new CancellationTokenSource();
        _ = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(900, ct).ConfigureAwait(true);
            await SaveSessionAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // further changes pending
        }
    }

    public async Task SaveSessionAsync(CancellationToken ct = default)
    {
        if (IsPrivateWindow) return;
        await _sessionSaveLock.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var tabs = Tabs.Select(t => t.ToSessionTab(isActive: t == SelectedTab)).ToList();
            await _sessionService.SaveAsync(tabs, isPrivateContext: false, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save session");
        }
        finally
        {
            _sessionSaveLock.Release();
        }
    }

    /// <summary>Called by MainWindow after a drag-reorder of the TabView strip.</summary>
    public void SyncTabOrder(IReadOnlyList<BrowserTabViewModel> ordered)
    {
        if (ordered.Count != Tabs.Count) return;
        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = Tabs.IndexOf(ordered[i]);
            if (currentIndex >= 0 && currentIndex != i)
                Tabs.Move(currentIndex, i);
        }
        NotifySessionDirty();
    }
}
