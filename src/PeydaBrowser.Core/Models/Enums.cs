namespace PeydaBrowser.Core.Models;

/// <summary>What the browser does when it starts.</summary>
public enum StartupBehavior
{
    RestorePreviousSession,
    OpenNewTabPage,
    OpenHomePage
}

/// <summary>User theme preference.</summary>
public enum ThemeModeSetting
{
    System,
    Light,
    Dark
}

/// <summary>UI density.</summary>
public enum UiDensity
{
    Compact,
    Normal,
    Comfortable
}

/// <summary>Where the tab strip is shown. Left (vertical tabs) is reserved for a later phase.</summary>
public enum TabPositionSetting
{
    Top,
    Left
}

/// <summary>Lifecycle state of a download.</summary>
public enum DownloadState
{
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Failed,
    Interrupted
}

/// <summary>Kinds of web permissions Peyda manages explicitly.</summary>
public enum BrowserPermissionKind
{
    Camera,
    Microphone,
    Geolocation,
    Notifications,
    ClipboardRead,
    Popups,
    AutomaticDownloads,
    Other
}

/// <summary>Stored decision for a site permission.</summary>
public enum PermissionDecision
{
    Ask,
    Allow,
    Deny
}

/// <summary>Predefined ranges for clearing browsing data.</summary>
public enum ClearTimeRange
{
    LastHour,
    Last24Hours,
    Last7Days,
    Last4Weeks,
    AllTime
}

public static class ClearTimeRangeExtensions
{
    /// <summary>Returns the UTC cutoff instant for the range; <c>null</c> means "all time".</summary>
    public static DateTimeOffset? GetCutoffUtc(this ClearTimeRange range, DateTimeOffset nowUtc) => range switch
    {
        ClearTimeRange.LastHour => nowUtc.AddHours(-1),
        ClearTimeRange.Last24Hours => nowUtc.AddHours(-24),
        ClearTimeRange.Last7Days => nowUtc.AddDays(-7),
        ClearTimeRange.Last4Weeks => nowUtc.AddDays(-28),
        _ => null
    };
}
