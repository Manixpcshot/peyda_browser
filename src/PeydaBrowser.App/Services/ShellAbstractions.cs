using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.WebEngine;

namespace PeydaBrowser.App.Services;

/// <summary>Operations a tab (or an internal page inside it) needs from the window shell.</summary>
public interface IBrowserShell
{
    BrowserSettings Settings { get; }
    bool IsPrivateWindow { get; }
    ILocalizationService Localization { get; }
    LocalizedStrings Strings { get; }
    DownloadManager Downloads { get; }

    Task<CoreWebView2Environment> GetWebViewEnvironmentAsync();
    void InvalidateWebViewEnvironment();

    void OpenNewTab(string? url = null, bool activate = true);
    void OpenInternalPageInNewTab(InternalPage page);
    void OpenPrivateWindow();
    void SetFullscreen(bool fullscreen);

    /// <summary>Asks the user (Windows save dialog) where a download should go; null = cancelled.</summary>
    Task<string?> PickDownloadPathAsync(string suggestedFileName);

    /// <summary>Confirmation dialog before an external protocol is handed to the OS.</summary>
    Task<bool> ConfirmExternalProtocolAsync(string url, XamlRoot xamlRoot);

    Task RecordHistoryAsync(string url, string title, string? faviconUrl);
    Task<bool> IsBookmarkedAsync(string url);
    Task ToggleBookmarkAsync(ViewModels.BrowserTabViewModel tab, XamlRoot xamlRoot);
    Task ClearBrowsingDataAsync(Core.Models.ClearTimeRange range, bool includeHistory, bool includeSiteData);
    void NotifySessionDirty();
    void NotifyBookmarksChanged();
}

/// <summary>The WebView2-hosting control surface used by <see cref="ViewModels.BrowserTabViewModel"/>.</summary>
public interface IBrowserTabView
{
    Task EnsureInitializedAsync();
    void NavigateTo(string url);
    void GoBack();
    void GoForward();
    void Reload(bool ignoreCache);
    void Stop();
    void SetMuted(bool muted);
    double ZoomFactor { get; set; }
    void OpenDevTools();
    void Print();
    Task ClearSiteDataAsync(Core.Models.ClearTimeRange range);
    void ApplyPreferredColorScheme(bool isDark);
    void CloseWebView();
    Task FocusWebContentAsync();
}
