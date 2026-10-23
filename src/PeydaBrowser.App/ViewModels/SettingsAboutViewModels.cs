using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Core.Updates;
using PeydaBrowser.Infrastructure.Files;
using PeydaBrowser.Infrastructure.Updates;
using PeydaBrowser.Theming;

namespace PeydaBrowser.App.ViewModels;

/// <summary>
/// Settings state. Controls bind two-way to these properties; the page calls
/// <see cref="QueueSave"/> from control events and the shell persists +
/// re-applies (theme, language, layout) via MainViewModel.ApplySettingsAsync.
/// </summary>
public sealed partial class SettingsViewModel : InternalPageViewModel
{
    private readonly MainViewModel _shell;
    private readonly SearchEngineService _searchEngines;
    private readonly ThemeService _themeService;
    private readonly ThemeFileService _themeFiles;
    private readonly IPermissionRepository _permissions;
    private CancellationTokenSource? _saveDebounce;
    private bool _loading;

    public SettingsViewModel(
        LocalizedStrings strings,
        MainViewModel shell,
        SearchEngineService searchEngines,
        ThemeService themeService,
        ThemeFileService themeFiles,
        IPermissionRepository permissions)
        : base(strings)
    {
        _shell = shell;
        _searchEngines = searchEngines;
        _themeService = themeService;
        _themeFiles = themeFiles;
        _permissions = permissions;
    }

    // General
    [ObservableProperty] public partial int StartupIndex { get; set; }
    [ObservableProperty] public partial string HomePage { get; set; } = string.Empty;

    // Search
    public ObservableCollection<SearchEngine> Engines { get; } = new();
    [ObservableProperty] public partial SearchEngine? DefaultEngine { get; set; }
    [ObservableProperty] public partial string NewEngineName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewEngineTemplate { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewEngineBang { get; set; } = string.Empty;
    [ObservableProperty] public partial string EngineError { get; set; } = string.Empty;

    // Appearance
    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial int ThemeModeIndex { get; set; }
    [ObservableProperty] public partial int DensityIndex { get; set; }
    public ObservableCollection<ThemeCatalogEntry> Themes { get; } = new();
    [ObservableProperty] public partial ThemeCatalogEntry? SelectedTheme { get; set; }
    [ObservableProperty] public partial string ThemeStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowBookmarkBar { get; set; }
    [ObservableProperty] public partial bool ShowSidebar { get; set; }

    // Downloads
    [ObservableProperty] public partial string DownloadFolder { get; set; } = string.Empty;
    [ObservableProperty] public partial bool AskWhereToSave { get; set; }

    // Privacy
    [ObservableProperty] public partial bool RecordHistory { get; set; }
    [ObservableProperty] public partial bool BlockPopups { get; set; }
    [ObservableProperty] public partial bool DevToolsEnabled { get; set; }
    [ObservableProperty] public partial bool TelemetryEnabled { get; set; }
    [ObservableProperty] public partial bool CheckUpdates { get; set; }
    [ObservableProperty] public partial int UpdateChannelIndex { get; set; }
    public ObservableCollection<SitePermission> Permissions { get; } = new();

    // New Tab
    [ObservableProperty] public partial bool NtShowClock { get; set; }
    [ObservableProperty] public partial bool NtShowDate { get; set; }
    [ObservableProperty] public partial bool NtShowLogo { get; set; }
    [ObservableProperty] public partial bool NtShowShortcuts { get; set; }
    [ObservableProperty] public partial bool NtShowGreeting { get; set; }
    [ObservableProperty] public partial string NtGreetingName { get; set; } = string.Empty;
    [ObservableProperty] public partial string NtBackgroundImage { get; set; } = string.Empty;

    public override async Task LoadAsync(string? argument, CancellationToken ct = default)
    {
        _loading = true;
        var s = _shell.Settings;
        StartupIndex = (int)s.StartupBehavior;
        HomePage = s.HomePage;
        LanguageIndex = s.Language == "fa" ? 1 : 0;
        ThemeModeIndex = (int)s.ThemeMode;
        DensityIndex = (int)s.Density;
        ShowBookmarkBar = s.ShowBookmarkBar;
        ShowSidebar = s.ShowSidebar;
        DownloadFolder = s.DownloadFolder;
        AskWhereToSave = s.AskWhereToSaveDownloads;
        RecordHistory = s.RecordHistory;
        BlockPopups = s.BlockPopups;
        DevToolsEnabled = s.DevToolsEnabled;
        TelemetryEnabled = s.TelemetryEnabled;
        CheckUpdates = s.CheckForUpdatesAutomatically;
        UpdateChannelIndex = s.UpdateChannel switch { "beta" => 1, "nightly" => 2, _ => 0 };
        NtShowClock = s.NewTabShowClock;
        NtShowDate = s.NewTabShowDate;
        NtShowLogo = s.NewTabShowLogo;
        NtShowShortcuts = s.NewTabShowShortcuts;
        NtShowGreeting = s.NewTabShowGreeting;
        NtGreetingName = s.NewTabGreetingName;
        NtBackgroundImage = s.NewTabBackgroundImagePath;

        await ReloadEnginesAsync(ct).ConfigureAwait(true);
        DefaultEngine = Engines.FirstOrDefault(e => e.Id == s.DefaultSearchEngineId) ?? Engines.FirstOrDefault();

        Themes.Clear();
        foreach (var theme in _themeService.AllThemes) Themes.Add(theme);
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == s.ThemeId) ?? Themes.FirstOrDefault();

        Permissions.Clear();
        foreach (var permission in await _permissions.GetAllAsync(ct).ConfigureAwait(true))
            Permissions.Add(permission);

        _loading = false;
        if (argument == "search")
            ScrollToSectionRequested?.Invoke(this, "search");
    }

    public event EventHandler<string>? ScrollToSectionRequested;

    private async Task ReloadEnginesAsync(CancellationToken ct = default)
    {
        Engines.Clear();
        foreach (var engine in await _searchEngines.GetAllAsync(ct).ConfigureAwait(true))
            Engines.Add(engine);
    }

    /// <summary>Called by the page whenever any settings control changes.</summary>
    public void QueueSave()
    {
        if (_loading) return;
        _saveDebounce?.Cancel();
        var cts = _saveDebounce = new CancellationTokenSource();
        _ = DelayedSaveAsync(cts.Token);
    }

    private async Task DelayedSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct).ConfigureAwait(true);
            await SaveNowAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SaveNowAsync(CancellationToken ct = default)
    {
        var previous = _shell.Settings;
        var updated = new BrowserSettings
        {
            StartupBehavior = (StartupBehavior)Math.Clamp(StartupIndex, 0, 2),
            HomePage = string.IsNullOrWhiteSpace(HomePage) ? BrowserSettings.DefaultHomePage : HomePage.Trim(),
            DefaultSearchEngineId = DefaultEngine?.Id ?? previous.DefaultSearchEngineId,
            ThemeMode = (ThemeModeSetting)Math.Clamp(ThemeModeIndex, 0, 2),
            ThemeId = SelectedTheme?.Id ?? previous.ThemeId,
            Language = LanguageIndex == 1 ? "fa" : "en",
            Density = (UiDensity)Math.Clamp(DensityIndex, 0, 2),
            TabPosition = previous.TabPosition,
            ShowBookmarkBar = ShowBookmarkBar,
            ShowSidebar = ShowSidebar,
            DownloadFolder = DownloadFolder?.Trim() ?? string.Empty,
            AskWhereToSaveDownloads = AskWhereToSave,
            RecordHistory = RecordHistory,
            BlockPopups = BlockPopups,
            OpenNewWindowRequestsInTab = previous.OpenNewWindowRequestsInTab,
            DevToolsEnabled = DevToolsEnabled,
            TelemetryEnabled = TelemetryEnabled,
            CrashReportingEnabled = previous.CrashReportingEnabled,
            CheckForUpdatesAutomatically = CheckUpdates,
            UpdateChannel = UpdateChannelIndex switch { 1 => "beta", 2 => "nightly", _ => "stable" },
            NewTabBackgroundImagePath = NtBackgroundImage?.Trim() ?? string.Empty,
            NewTabBackgroundColor = previous.NewTabBackgroundColor,
            NewTabShowClock = NtShowClock,
            NewTabShowDate = NtShowDate,
            NewTabShowLogo = NtShowLogo,
            NewTabShowShortcuts = NtShowShortcuts,
            NewTabShowGreeting = NtShowGreeting,
            NewTabGreetingName = NtGreetingName?.Trim() ?? string.Empty
        };
        await _shell.ApplySettingsAsync(updated, ct).ConfigureAwait(true);
    }

    partial void OnSelectedThemeChanged(ThemeCatalogEntry? value)
    {
        if (_loading || value is null) return;
        // Selecting a theme also aligns the theme mode so surfaces stay coherent.
        ThemeModeIndex = value.Definition.Mode == "light" ? (int)ThemeModeSetting.Light : (int)ThemeModeSetting.Dark;
        QueueSave();
    }

    partial void OnDefaultEngineChanged(SearchEngine? value) => QueueSave();

    // ---- search engine management ----

    [RelayCommand]
    private async Task AddEngineAsync()
    {
        EngineError = string.Empty;
        var engine = new SearchEngine
        {
            Id = SearchEngineService.NewId(),
            Name = NewEngineName?.Trim() ?? string.Empty,
            UrlTemplate = NewEngineTemplate?.Trim() ?? string.Empty,
            Bang = string.IsNullOrWhiteSpace(NewEngineBang) ? null : NewEngineBang.Trim(),
            SortOrder = Engines.Count,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        var error = await _searchEngines.SaveAsync(engine).ConfigureAwait(true);
        if (error is not null)
        {
            EngineError = error;
            return;
        }
        NewEngineName = NewEngineTemplate = NewEngineBang = string.Empty;
        await ReloadEnginesAsync().ConfigureAwait(true);
        await _shell.RefreshSearchEnginesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteEngineAsync(SearchEngine? engine)
    {
        if (engine is null) return;
        try
        {
            await _searchEngines.DeleteAsync(engine.Id).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            EngineError = ex.Message;
            return;
        }
        await ReloadEnginesAsync().ConfigureAwait(true);
        if (DefaultEngine is null || DefaultEngine.Id == engine.Id)
            DefaultEngine = Engines.FirstOrDefault();
        await _shell.RefreshSearchEnginesAsync().ConfigureAwait(true);
        QueueSave();
    }

    public async Task MoveEngineAsync(SearchEngine engine, int delta)
    {
        var index = Engines.IndexOf(engine);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Engines.Count) return;
        Engines.Move(index, target);
        await _searchEngines.SetSortOrderAsync(Engines.Select(e => e.Id).ToList()).ConfigureAwait(true);
        await _shell.RefreshSearchEnginesAsync().ConfigureAwait(true);
    }

    public bool IsEngineSecure(SearchEngine engine) => SearchUrlBuilder.IsSecureTemplate(engine.UrlTemplate);

    // ---- themes import/export (paths resolved by the page via pickers) ----

    public async Task ImportThemeAsync(string path)
    {
        var result = await _themeFiles.ImportAsync(path).ConfigureAwait(true);
        if (result.Errors.Contains("theme.json.invalid") || result.Errors.Contains("theme.file.missing") || result.Errors.Contains("theme.file.tooLarge"))
        {
            ThemeStatus = string.Join(", ", result.Errors);
            return;
        }
        var id = await _themeService.SaveImportedThemeAsync(result.Theme).ConfigureAwait(true);
        Themes.Clear();
        foreach (var theme in _themeService.AllThemes) Themes.Add(theme);
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == id);
        ThemeStatus = result.IsValid ? string.Empty : string.Join(", ", result.Errors);
    }

    public async Task ExportThemeAsync(string path)
    {
        if (SelectedTheme is null) return;
        await _themeFiles.ExportAsync(SelectedTheme.Definition, path).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteSelectedThemeAsync()
    {
        if (SelectedTheme is null || SelectedTheme.IsBuiltIn) return;
        await _themeService.DeleteCustomThemeAsync(SelectedTheme.Id).ConfigureAwait(true);
        Themes.Remove(SelectedTheme);
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == BuiltInThemes.DarkId) ?? Themes.FirstOrDefault();
    }

    public bool CanDeleteSelectedTheme => SelectedTheme is { IsBuiltIn: false };

    // ---- permissions ----

    [RelayCommand]
    private async Task DeletePermissionAsync(SitePermission? permission)
    {
        if (permission is null) return;
        await _permissions.DeleteAsync(permission.Origin, permission.Kind).ConfigureAwait(true);
        Permissions.Remove(permission);
    }

    // ---- clear data ----

    public Task ClearDataAsync(ClearTimeRange range, bool history, bool siteData) =>
        _shell.ClearBrowsingDataAsync(range, history, siteData);
}

public sealed partial class AboutViewModel : InternalPageViewModel
{
    private readonly MainViewModel _shell;
    private readonly GitHubUpdateService _updateService;

    public AboutViewModel(LocalizedStrings strings, MainViewModel shell, GitHubUpdateService updateService)
        : base(strings)
    {
        _shell = shell;
        _updateService = updateService;
    }

    [ObservableProperty] public partial string VersionText { get; set; } = string.Empty;
    [ObservableProperty] public partial string WebView2Text { get; set; } = string.Empty;
    [ObservableProperty] public partial string UpdateStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string? UpdateReleaseUrl { get; set; }

    public override Task LoadAsync(string? argument, CancellationToken ct = default)
    {
        var version = GitHubUpdateService.GetInformationalVersion(typeof(App).Assembly);
        VersionText = Strings.Format(LocKeys.AboutVersion, version);

        string? runtime = null;
        try
        {
            runtime = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            runtime = null;
        }
        WebView2Text = runtime is null
            ? Strings.AboutWebView2NotFound
            : Strings.Format(LocKeys.AboutWebView2Runtime, runtime);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = string.Empty;
        UpdateReleaseUrl = null;
        var channel = UpdateChannelParser.Parse(_shell.Settings.UpdateChannel);
        var current = GitHubUpdateService.GetCurrentVersion(typeof(App).Assembly);
        var result = await _updateService.CheckForUpdatesAsync(current, channel).ConfigureAwait(true);

        if (!result.Success)
        {
            UpdateStatus = Strings.UpdateFailed;
        }
        else if (result.AvailableUpdate is not null)
        {
            UpdateStatus = Strings.Format(LocKeys.UpdateAvailable, result.AvailableVersion?.ToString() ?? result.AvailableUpdate.TagName);
            UpdateReleaseUrl = result.AvailableUpdate.HtmlUrl;
        }
        else
        {
            UpdateStatus = Strings.UpdateNone;
        }
        OnPropertyChanged(nameof(HasUpdateRelease));
    }

    public bool HasUpdateRelease => !string.IsNullOrEmpty(UpdateReleaseUrl);

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = UpdateReleaseUrl ?? _updateService.ReleasesPageUrl;
        _shell.OpenNewTab(url, activate: true);
    }
}

public sealed class PrivateHomeViewModel(LocalizedStrings strings) : InternalPageViewModel(strings);
