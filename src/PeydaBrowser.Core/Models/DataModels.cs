namespace PeydaBrowser.Core.Models;

/// <summary>One aggregated history row per URL.</summary>
public sealed class HistoryEntry
{
    public long Id { get; set; }
    public required string Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public DateTimeOffset FirstVisitUtc { get; set; }
    public DateTimeOffset LastVisitUtc { get; set; }
    public string? FaviconUrl { get; set; }
}

public sealed class BookmarkFolder
{
    public required string Id { get; init; }
    public string? ParentId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class Bookmark
{
    public required string Id { get; init; }
    public string? FolderId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public string? FaviconUrl { get; set; }
}

/// <summary>Persisted information about a finished or interrupted download.</summary>
public sealed class DownloadRecord
{
    public required string Id { get; init; }
    public required string FileName { get; set; }
    public required string SourceUrl { get; set; }
    public required string TargetPath { get; set; }
    public long TotalBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public DownloadState State { get; set; } = DownloadState.InProgress;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string? Sha256 { get; set; }
}

/// <summary>
/// A browser profile descriptor. Each profile owns an isolated data folder
/// (SQLite database + WebView2 user data), so history, cookies, bookmarks
/// and settings never leak between profiles.
/// </summary>
public sealed class BrowserProfile
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string ColorHex { get; set; } = "#7C3AED";
    public DateTimeOffset CreatedUtc { get; init; }
    public bool IsGuest { get; init; }

    /// <summary>True for ephemeral private/guest contexts whose data must be wiped on exit.</summary>
    public bool IsEphemeral { get; init; }

    public const string DefaultProfileId = "default";
}

/// <summary>A shortcut tile shown on the New Tab page.</summary>
public sealed class NewTabShortcut
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SessionTab
{
    public required string Url { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
    public bool IsMuted { get; init; }
    public bool IsActive { get; init; }
}

public sealed class SessionSnapshot
{
    public required string Id { get; init; }
    public string Name { get; init; } = "auto";
    public DateTimeOffset CreatedUtc { get; init; }
    public IReadOnlyList<SessionTab> Tabs { get; init; } = Array.Empty<SessionTab>();
}

public sealed class ClosedTab
{
    public long Id { get; set; }
    public required string Url { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
    public bool IsMuted { get; init; }
    public DateTimeOffset ClosedUtc { get; init; }
}

/// <summary>A remembered permission decision for one origin.</summary>
public sealed class SitePermission
{
    public required string Origin { get; init; }
    public BrowserPermissionKind Kind { get; init; }
    public PermissionDecision Decision { get; set; } = PermissionDecision.Ask;
    public DateTimeOffset UpdatedUtc { get; set; }
}

public enum OmniboxSuggestionKind
{
    History,
    Bookmark,
    Search
}

public sealed class OmniboxSuggestion
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public OmniboxSuggestionKind Kind { get; init; }
    public int VisitCount { get; init; }
    public string DisplayText => string.IsNullOrWhiteSpace(Title) ? Url : Title;
}
