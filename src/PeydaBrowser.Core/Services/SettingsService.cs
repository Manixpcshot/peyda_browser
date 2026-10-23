using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;

namespace PeydaBrowser.Core.Services;

/// <summary>
/// Strongly-typed settings facade over the key/value store. Values coming
/// back from disk are treated as untrusted: anything unparsable or unsafe
/// falls back to the built-in default instead of throwing.
/// </summary>
public sealed class SettingsService
{
    private readonly ISettingsRepository _repository;

    public SettingsService(ISettingsRepository repository) => _repository = repository;

    private static class Keys
    {
        public const string StartupBehavior = "general.startupBehavior";
        public const string HomePage = "general.homePage";
        public const string DefaultSearchEngineId = "search.defaultEngineId";
        public const string ThemeMode = "appearance.themeMode";
        public const string ThemeId = "appearance.themeId";
        public const string Language = "appearance.language";
        public const string Density = "appearance.density";
        public const string TabPosition = "appearance.tabPosition";
        public const string ShowBookmarkBar = "appearance.showBookmarkBar";
        public const string ShowSidebar = "appearance.showSidebar";
        public const string DownloadFolder = "downloads.folder";
        public const string AskWhereToSave = "downloads.askWhereToSave";
        public const string RecordHistory = "privacy.recordHistory";
        public const string BlockPopups = "privacy.blockPopups";
        public const string NewWindowInTab = "privacy.newWindowInTab";
        public const string DevToolsEnabled = "privacy.devToolsEnabled";
        public const string TelemetryEnabled = "privacy.telemetryEnabled";
        public const string CrashReportingEnabled = "privacy.crashReportingEnabled";
        public const string CheckUpdates = "updates.checkAutomatically";
        public const string UpdateChannel = "updates.channel";
        public const string NtBackgroundImage = "newtab.backgroundImagePath";
        public const string NtBackgroundColor = "newtab.backgroundColor";
        public const string NtShowClock = "newtab.showClock";
        public const string NtShowDate = "newtab.showDate";
        public const string NtShowLogo = "newtab.showLogo";
        public const string NtShowShortcuts = "newtab.showShortcuts";
        public const string NtShowGreeting = "newtab.showGreeting";
        public const string NtGreetingName = "newtab.greetingName";
    }

    public async Task<BrowserSettings> LoadAsync(CancellationToken ct = default)
    {
        var raw = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var s = new BrowserSettings();
        s.StartupBehavior = GetEnum(raw, Keys.StartupBehavior, s.StartupBehavior);
        s.HomePage = GetString(raw, Keys.HomePage, s.HomePage, IsSafeHomePage);
        s.DefaultSearchEngineId = GetString(raw, Keys.DefaultSearchEngineId, s.DefaultSearchEngineId, v => v.Length <= 64);
        s.ThemeMode = GetEnum(raw, Keys.ThemeMode, s.ThemeMode);
        s.ThemeId = GetString(raw, Keys.ThemeId, s.ThemeId, v => v.Length <= 64);
        s.Language = GetString(raw, Keys.Language, s.Language, v => v is "en" or "fa");
        s.Density = GetEnum(raw, Keys.Density, s.Density);
        s.TabPosition = GetEnum(raw, Keys.TabPosition, s.TabPosition);
        s.ShowBookmarkBar = GetBool(raw, Keys.ShowBookmarkBar, s.ShowBookmarkBar);
        s.ShowSidebar = GetBool(raw, Keys.ShowSidebar, s.ShowSidebar);
        s.DownloadFolder = GetString(raw, Keys.DownloadFolder, s.DownloadFolder, v => v.Length <= 1024 && !v.Any(char.IsControl));
        s.AskWhereToSaveDownloads = GetBool(raw, Keys.AskWhereToSave, s.AskWhereToSaveDownloads);
        s.RecordHistory = GetBool(raw, Keys.RecordHistory, s.RecordHistory);
        s.BlockPopups = GetBool(raw, Keys.BlockPopups, s.BlockPopups);
        s.OpenNewWindowRequestsInTab = GetBool(raw, Keys.NewWindowInTab, s.OpenNewWindowRequestsInTab);
        s.DevToolsEnabled = GetBool(raw, Keys.DevToolsEnabled, s.DevToolsEnabled);
        s.TelemetryEnabled = GetBool(raw, Keys.TelemetryEnabled, s.TelemetryEnabled);
        s.CrashReportingEnabled = GetBool(raw, Keys.CrashReportingEnabled, s.CrashReportingEnabled);
        s.CheckForUpdatesAutomatically = GetBool(raw, Keys.CheckUpdates, s.CheckForUpdatesAutomatically);
        s.UpdateChannel = GetString(raw, Keys.UpdateChannel, s.UpdateChannel, v => v is "stable" or "beta" or "nightly");
        s.NewTabBackgroundImagePath = GetString(raw, Keys.NtBackgroundImage, s.NewTabBackgroundImagePath, v => v.Length <= 1024 && !v.Any(char.IsControl));
        s.NewTabBackgroundColor = GetString(raw, Keys.NtBackgroundColor, s.NewTabBackgroundColor, v => v.Length <= 16);
        s.NewTabShowClock = GetBool(raw, Keys.NtShowClock, s.NewTabShowClock);
        s.NewTabShowDate = GetBool(raw, Keys.NtShowDate, s.NewTabShowDate);
        s.NewTabShowLogo = GetBool(raw, Keys.NtShowLogo, s.NewTabShowLogo);
        s.NewTabShowShortcuts = GetBool(raw, Keys.NtShowShortcuts, s.NewTabShowShortcuts);
        s.NewTabShowGreeting = GetBool(raw, Keys.NtShowGreeting, s.NewTabShowGreeting);
        s.NewTabGreetingName = GetString(raw, Keys.NtGreetingName, s.NewTabGreetingName, v => v.Length <= 60);
        return s;
    }

    public async Task SaveAsync(BrowserSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await Set(Keys.StartupBehavior, settings.StartupBehavior.ToString(), ct).ConfigureAwait(false);
        await Set(Keys.HomePage, IsSafeHomePage(settings.HomePage) ? settings.HomePage : BrowserSettings.DefaultHomePage, ct).ConfigureAwait(false);
        await Set(Keys.DefaultSearchEngineId, settings.DefaultSearchEngineId, ct).ConfigureAwait(false);
        await Set(Keys.ThemeMode, settings.ThemeMode.ToString(), ct).ConfigureAwait(false);
        await Set(Keys.ThemeId, settings.ThemeId, ct).ConfigureAwait(false);
        await Set(Keys.Language, settings.Language is "en" or "fa" ? settings.Language : "en", ct).ConfigureAwait(false);
        await Set(Keys.Density, settings.Density.ToString(), ct).ConfigureAwait(false);
        await Set(Keys.TabPosition, settings.TabPosition.ToString(), ct).ConfigureAwait(false);
        await Set(Keys.ShowBookmarkBar, settings.ShowBookmarkBar, ct).ConfigureAwait(false);
        await Set(Keys.ShowSidebar, settings.ShowSidebar, ct).ConfigureAwait(false);
        await Set(Keys.DownloadFolder, settings.DownloadFolder ?? string.Empty, ct).ConfigureAwait(false);
        await Set(Keys.AskWhereToSave, settings.AskWhereToSaveDownloads, ct).ConfigureAwait(false);
        await Set(Keys.RecordHistory, settings.RecordHistory, ct).ConfigureAwait(false);
        await Set(Keys.BlockPopups, settings.BlockPopups, ct).ConfigureAwait(false);
        await Set(Keys.NewWindowInTab, settings.OpenNewWindowRequestsInTab, ct).ConfigureAwait(false);
        await Set(Keys.DevToolsEnabled, settings.DevToolsEnabled, ct).ConfigureAwait(false);
        await Set(Keys.TelemetryEnabled, settings.TelemetryEnabled, ct).ConfigureAwait(false);
        await Set(Keys.CrashReportingEnabled, settings.CrashReportingEnabled, ct).ConfigureAwait(false);
        await Set(Keys.CheckUpdates, settings.CheckForUpdatesAutomatically, ct).ConfigureAwait(false);
        await Set(Keys.UpdateChannel, settings.UpdateChannel, ct).ConfigureAwait(false);
        await Set(Keys.NtBackgroundImage, settings.NewTabBackgroundImagePath ?? string.Empty, ct).ConfigureAwait(false);
        await Set(Keys.NtBackgroundColor, settings.NewTabBackgroundColor ?? string.Empty, ct).ConfigureAwait(false);
        await Set(Keys.NtShowClock, settings.NewTabShowClock, ct).ConfigureAwait(false);
        await Set(Keys.NtShowDate, settings.NewTabShowDate, ct).ConfigureAwait(false);
        await Set(Keys.NtShowLogo, settings.NewTabShowLogo, ct).ConfigureAwait(false);
        await Set(Keys.NtShowShortcuts, settings.NewTabShowShortcuts, ct).ConfigureAwait(false);
        await Set(Keys.NtShowGreeting, settings.NewTabShowGreeting, ct).ConfigureAwait(false);
        await Set(Keys.NtGreetingName, settings.NewTabGreetingName ?? string.Empty, ct).ConfigureAwait(false);
    }

    private Task Set(string key, string value, CancellationToken ct) => _repository.SetAsync(key, value, ct);
    private Task Set(string key, bool value, CancellationToken ct) => _repository.SetAsync(key, value ? "true" : "false", ct);

    private static string GetString(IReadOnlyDictionary<string, string> raw, string key, string fallback, Func<string, bool>? validate = null)
    {
        if (!raw.TryGetValue(key, out var value) || value is null) return fallback;
        return validate is null || validate(value) ? value : fallback;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> raw, string key, bool fallback) =>
        raw.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static T GetEnum<T>(IReadOnlyDictionary<string, string> raw, string key, T fallback) where T : struct, Enum =>
        raw.TryGetValue(key, out var value) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static bool IsSafeHomePage(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= UrlValidator.MaxUrlLength &&
        (PeydaUrl.IsPeydaUrl(value) || UrlValidator.IsValidHttpUrl(value, out _));
}
