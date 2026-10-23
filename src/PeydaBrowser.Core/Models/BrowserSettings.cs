namespace PeydaBrowser.Core.Models;

/// <summary>
/// Typed, validated view over the key/value Settings table. Defaults are
/// privacy-respecting: telemetry and crash reporting are off, history
/// recording is on (local only) and can be disabled by the user.
/// </summary>
public sealed class BrowserSettings
{
    public const string DefaultHomePage = PeydaUrl.NewTab;

    // General
    public StartupBehavior StartupBehavior { get; set; } = StartupBehavior.RestorePreviousSession;
    public string HomePage { get; set; } = DefaultHomePage;

    // Search
    public string DefaultSearchEngineId { get; set; } = SearchEngine.BuiltIn.GoogleId;

    // Appearance
    public ThemeModeSetting ThemeMode { get; set; } = ThemeModeSetting.System;
    public string ThemeId { get; set; } = "peyda-dark";
    public string Language { get; set; } = "en";
    public UiDensity Density { get; set; } = UiDensity.Normal;
    public TabPositionSetting TabPosition { get; set; } = TabPositionSetting.Top;
    public bool ShowBookmarkBar { get; set; } = true;
    public bool ShowSidebar { get; set; } = true;

    // Downloads
    public string DownloadFolder { get; set; } = string.Empty;
    public bool AskWhereToSaveDownloads { get; set; }

    // Privacy & security
    public bool RecordHistory { get; set; } = true;
    public bool BlockPopups { get; set; } = true;
    public bool OpenNewWindowRequestsInTab { get; set; } = true;
    public bool DevToolsEnabled { get; set; }
    public bool TelemetryEnabled { get; set; }
    public bool CrashReportingEnabled { get; set; }
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable";

    // New Tab page
    public string NewTabBackgroundImagePath { get; set; } = string.Empty;
    public string NewTabBackgroundColor { get; set; } = string.Empty;
    public bool NewTabShowClock { get; set; } = true;
    public bool NewTabShowDate { get; set; } = true;
    public bool NewTabShowLogo { get; set; } = true;
    public bool NewTabShowShortcuts { get; set; } = true;
    public bool NewTabShowGreeting { get; set; } = true;
    public string NewTabGreetingName { get; set; } = string.Empty;
}

/// <summary>Canonical internal URLs. These are routed inside the app and are never
/// registered as an operating-system protocol handler, which keeps the attack surface small.</summary>
public static class PeydaUrl
{
    public const string Scheme = "peyda";
    public const string NewTab = "peyda://newtab";
    public const string Settings = "peyda://settings";
    public const string History = "peyda://history";
    public const string Downloads = "peyda://downloads";
    public const string Bookmarks = "peyda://bookmarks";
    public const string About = "peyda://about";
    public const string Private = "peyda://private";

    public static bool IsPeydaUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the page name from a peyda:// URL ("" when malformed).</summary>
    public static string GetPageName(string url)
    {
        if (!IsPeydaUrl(url)) return string.Empty;
        var rest = url[(Scheme.Length + 3)..];
        var end = rest.IndexOfAny(new[] { '/', '?', '#' });
        if (end >= 0) rest = rest[..end];
        return rest.Trim().ToLowerInvariant();
    }
}
