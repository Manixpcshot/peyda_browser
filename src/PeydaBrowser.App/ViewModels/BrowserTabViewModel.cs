using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.WebEngine;

namespace PeydaBrowser.App.ViewModels;

/// <summary>
/// State of one browser tab. The actual WebView2 control lives in
/// BrowserTabView (attached via <see cref="View"/>); this view model owns
/// address-bar logic, internal-page switching, error/crash presentation and
/// per-tab navigation state.
/// </summary>
public sealed partial class BrowserTabViewModel : ObservableObject
{
    private readonly AddressBarParser _parser;
    private readonly SearchEngineService _searchEngines;
    private readonly OmniboxSuggestionService _suggestions;
    private CancellationTokenSource? _suggestionCts;
    private bool _suppressAddressSuggestion;

    public BrowserTabViewModel(
        IBrowserShell shell,
        AddressBarParser parser,
        SearchEngineService searchEngines,
        OmniboxSuggestionService suggestions)
    {
        Shell = shell;
        _parser = parser;
        _searchEngines = searchEngines;
        _suggestions = suggestions;
    }

    public IBrowserShell Shell { get; }
    public IBrowserTabView? View { get; set; }
    public Guid Id { get; } = Guid.NewGuid();

    public event EventHandler? FocusAddressBarRequested;
    public event EventHandler? InternalPageChanged;

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrentUrl { get; set; } = PeydaUrl.NewTab;
    [ObservableProperty] public partial string AddressText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    [ObservableProperty] public partial bool IsBookmarked { get; set; }
    [ObservableProperty] public partial BitmapImage? Favicon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWebPage))]
    public partial InternalPage? CurrentInternalPage { get; set; }

    public string? InternalPageArgument { get; private set; }

    /// <summary>Last real web URL shown in this tab; used to return from an overlaid internal page.</summary>
    public string LastWebUrl { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorPage))]
    public partial PageErrorKind? ErrorKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorPage))]
    public partial bool IsCrashed { get; set; }

    public bool ShowErrorPage => ErrorKind is not null || IsCrashed;
    public bool IsWebPage => CurrentInternalPage is null && !ShowErrorPage;

    [ObservableProperty] public partial UrlDisplayInfo? SecurityInfo { get; set; }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                View?.SetMuted(value);
                Shell.NotifySessionDirty();
            }
        }
    }

    public ObservableCollection<OmniboxSuggestion> Suggestions { get; } = new();

    /// <summary>Overrides the default engine for this tab's next searches (omnibox engine button).</summary>
    public string? SessionSearchEngineId { get; set; }

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title;
            if (CurrentInternalPage is not null) return InternalPageTitle(CurrentInternalPage.Value);
            return Uri.TryCreate(CurrentUrl, UriKind.Absolute, out var uri) ? uri.Host : Shell.Strings.TabNew;
        }
    }

    private string InternalPageTitle(InternalPage page) => page switch
    {
        InternalPage.NewTab => Shell.Strings.TabNew,
        InternalPage.Settings => Shell.Strings.SettingsTitle,
        InternalPage.History => Shell.Strings.HistoryTitle,
        InternalPage.Downloads => Shell.Strings.DownloadsTitle,
        InternalPage.Bookmarks => Shell.Strings.BookmarksTitle,
        InternalPage.About => Shell.Strings.MenuAbout,
        InternalPage.PrivateHome => Shell.Strings.PrivateHeading,
        _ => Shell.Strings.TabNew
    };

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
    partial void OnCurrentUrlChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));
    partial void OnCurrentInternalPageChanged(InternalPage? value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        InternalPageChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnErrorKindChanged(PageErrorKind? value) => OnPropertyChanged(nameof(IsWebPage));
    partial void OnIsCrashedChanged(bool value) => OnPropertyChanged(nameof(IsWebPage));

    // ----- Navigation API used by shell, pages and the toolbar -----

    public void NavigateToUrl(string url)
    {
        if (InternalPageRouter.TryRoute(url, out var route))
        {
            ShowInternalPage(route);
            return;
        }
        ShowWebPage();
        CurrentUrl = url;
        LastWebUrl = url;
        SetAddressTextSilently(url);
        View?.NavigateTo(url);
    }

    public async Task SubmitAddressAsync(string input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return;

        var bangMap = await _searchEngines.GetBangMapAsync().ConfigureAwait(true);
        var result = _parser.Parse(text, bangMap);

        switch (result.Kind)
        {
            case AddressBarInputKind.Url:
                NavigateToUrl(result.Target);
                break;
            case AddressBarInputKind.InternalPage:
                NavigateToUrl(result.Target);
                break;
            case AddressBarInputKind.Search:
                var engine = await _searchEngines.ResolveAsync(
                    result.SearchEngineId ?? SessionSearchEngineId,
                    Shell.Settings.DefaultSearchEngineId).ConfigureAwait(true);
                NavigateToUrl(SearchUrlBuilder.BuildSearchUrl(engine, result.Query ?? text));
                break;
        }
        Shell.NotifySessionDirty();
    }

    /// <summary>"Paste and search": always treats the text as a query for the active engine.</summary>
    public async Task SearchWithDefaultAsync(string query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0) return;
        var engine = await _searchEngines.ResolveAsync(SessionSearchEngineId, Shell.Settings.DefaultSearchEngineId).ConfigureAwait(true);
        NavigateToUrl(SearchUrlBuilder.BuildSearchUrl(engine, text));
    }

    public void ShowInternalPage(InternalRoute route)
    {
        ErrorKind = null;
        IsCrashed = false;
        InternalPageArgument = route.Argument;
        if (route.Page == InternalPage.Unknown)
        {
            CurrentInternalPage = InternalPage.Error;
            CurrentUrl = PeydaUrl.Scheme + "://error";
        }
        else
        {
            CurrentInternalPage = route.Page;
            CurrentUrl = InternalPageRouter.ToUrl(route.Page);
        }
        IsLoading = false;
        SetAddressTextSilently(CurrentInternalPage == InternalPage.NewTab ? string.Empty : CurrentUrl);
        UpdateSecurityInfo();
    }

    public void ShowWebPage()
    {
        CurrentInternalPage = null;
        ErrorKind = null;
        IsCrashed = false;
    }

    public void ShowError(PageErrorKind kind)
    {
        CurrentInternalPage = null;
        ErrorKind = kind;
        IsLoading = false;
    }

    public void ShowCrashed()
    {
        CurrentInternalPage = null;
        IsCrashed = true;
        IsLoading = false;
    }

    // ----- Callbacks from BrowserTabView -----

    public void OnNavigationStarted(string url)
    {
        IsLoading = true;
        ErrorKind = null;
        IsCrashed = false;
    }

    public void OnSourceChanged(string url)
    {
        CurrentUrl = url;
        LastWebUrl = url;
        SetAddressTextSilently(url);
        UpdateSecurityInfo();
    }

    /// <summary>Returns from an overlaid internal page to the web page underneath.</summary>
    public bool ReturnToWebPage()
    {
        if (CurrentInternalPage is null || string.IsNullOrEmpty(LastWebUrl)) return false;
        ShowWebPage();
        CurrentUrl = LastWebUrl;
        SetAddressTextSilently(LastWebUrl);
        UpdateSecurityInfo();
        return true;
    }

    public async Task OnNavigationCompletedAsync(bool success, PageErrorKind? error, string url, string title, string? faviconUrl)
    {
        IsLoading = false;
        if (!success && error is not null)
        {
            ShowError(error.Value);
            return;
        }
        if (success && !string.IsNullOrWhiteSpace(url))
        {
            await Shell.RecordHistoryAsync(url, title, faviconUrl).ConfigureAwait(true);
            Shell.NotifySessionDirty();
        }
    }

    public void OnTitleChangedFromEngine(string title) => Title = title ?? string.Empty;

    public void OnHistoryStateChanged(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    public void UpdateSecurityInfo() => SecurityInfo = UrlDisplaySafety.Describe(CurrentUrl);

    public void SetAddressTextSilently(string text)
    {
        _suppressAddressSuggestion = true;
        AddressText = text;
        _suppressAddressSuggestion = false;
    }

    partial void OnAddressTextChanged(string value)
    {
        if (_suppressAddressSuggestion) return;
        _ = RefreshSuggestionsAsync(value);
    }

    private async Task RefreshSuggestionsAsync(string text)
    {
        _suggestionCts?.Cancel();
        var cts = _suggestionCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token).ConfigureAwait(true);
            var results = await _suggestions.GetSuggestionsAsync(text?.Trim() ?? string.Empty, 8, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;
            Suggestions.Clear();
            foreach (var suggestion in results) Suggestions.Add(suggestion);
        }
        catch (OperationCanceledException)
        {
            // superseded by newer input
        }
    }

    public void RequestFocusAddressBar() => FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);

    public SessionTab ToSessionTab(bool isActive) => new()
    {
        Url = CurrentUrl,
        Title = DisplayTitle,
        IsPinned = IsPinned,
        IsMuted = IsMuted,
        IsActive = isActive
    };

    // ----- Commands used by toolbar/menus -----

    [RelayCommand] private void GoBack() => View?.GoBack();
    [RelayCommand] private void GoForward() => View?.GoForward();

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload() => DoReload(ignoreCache: false);
    private bool CanReload() => !IsLoading;

    [RelayCommand] private void HardReload() => DoReload(ignoreCache: true);
    [RelayCommand] private void Stop() { View?.Stop(); IsLoading = false; }

    public void DoReload(bool ignoreCache)
    {
        if (CurrentInternalPage is not null)
        {
            // Re-raise so the hosted page control is recreated with fresh data.
            var page = CurrentInternalPage;
            CurrentInternalPage = null;
            CurrentInternalPage = page;
            return;
        }
        ErrorKind = null;
        IsCrashed = false;
        View?.Reload(ignoreCache);
    }

    [RelayCommand]
    private void GoHome()
    {
        var home = Shell.Settings.HomePage;
        NavigateToUrl(string.IsNullOrWhiteSpace(home) ? PeydaUrl.NewTab : home);
    }

    public void ZoomIn() { if (View is not null) View.ZoomFactor = Math.Min(4.0, View.ZoomFactor * 1.15); }
    public void ZoomOut() { if (View is not null) View.ZoomFactor = Math.Max(0.3, View.ZoomFactor / 1.15); }
    public void ZoomReset() { if (View is not null) View.ZoomFactor = 1.0; }
}
