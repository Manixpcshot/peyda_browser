namespace PeydaBrowser.Persistence.Migrations;

/// <summary>An immutable, forward-only schema migration. Once shipped, a
/// migration's SQL must never be edited; add a new version instead.</summary>
public sealed record SchemaMigration(int Version, string Name, string Sql);

public static class SchemaMigrations
{
    public static IReadOnlyList<SchemaMigration> All { get; } = new[] { V1, V2 };
    public static int LatestVersion => All[^1].Version;

    private static readonly SchemaMigration V1 = new(1, "Initial schema", """
        CREATE TABLE IF NOT EXISTS Settings (
            Key        TEXT PRIMARY KEY,
            Value      TEXT NOT NULL,
            UpdatedUtc INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SearchEngines (
            Id          TEXT PRIMARY KEY,
            Name        TEXT NOT NULL,
            UrlTemplate TEXT NOT NULL,
            Bang        TEXT NULL,
            IconUrl     TEXT NULL,
            SortOrder   INTEGER NOT NULL DEFAULT 0,
            IsBuiltIn   INTEGER NOT NULL DEFAULT 0,
            CreatedUtc  INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS History (
            Id            INTEGER PRIMARY KEY AUTOINCREMENT,
            Url           TEXT NOT NULL UNIQUE,
            Title         TEXT NOT NULL DEFAULT '',
            VisitCount    INTEGER NOT NULL DEFAULT 1,
            FirstVisitUtc INTEGER NOT NULL,
            LastVisitUtc  INTEGER NOT NULL,
            FaviconUrl    TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_History_LastVisit ON History (LastVisitUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_History_Title ON History (Title);

        CREATE TABLE IF NOT EXISTS BookmarkFolders (
            Id         TEXT PRIMARY KEY,
            ParentId   TEXT NULL REFERENCES BookmarkFolders(Id) ON DELETE CASCADE,
            Name       TEXT NOT NULL,
            SortOrder  INTEGER NOT NULL DEFAULT 0,
            CreatedUtc INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Bookmarks (
            Id          TEXT PRIMARY KEY,
            FolderId    TEXT NULL REFERENCES BookmarkFolders(Id) ON DELETE CASCADE,
            Title       TEXT NOT NULL,
            Url         TEXT NOT NULL,
            SortOrder   INTEGER NOT NULL DEFAULT 0,
            CreatedUtc  INTEGER NOT NULL,
            ModifiedUtc INTEGER NOT NULL,
            FaviconUrl  TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Bookmarks_Folder_Url ON Bookmarks (COALESCE(FolderId, ''), Url);
        CREATE INDEX IF NOT EXISTS IX_Bookmarks_Folder ON Bookmarks (FolderId);

        CREATE TABLE IF NOT EXISTS Downloads (
            Id            TEXT PRIMARY KEY,
            FileName      TEXT NOT NULL,
            SourceUrl     TEXT NOT NULL,
            TargetPath    TEXT NOT NULL,
            TotalBytes    INTEGER NOT NULL DEFAULT 0,
            ReceivedBytes INTEGER NOT NULL DEFAULT 0,
            State         TEXT NOT NULL DEFAULT 'InProgress',
            StartedUtc    INTEGER NOT NULL,
            CompletedUtc  INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS Sessions (
            Id         TEXT PRIMARY KEY,
            Name       TEXT NOT NULL,
            CreatedUtc INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SessionTabs (
            SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
            Position  INTEGER NOT NULL,
            Url       TEXT NOT NULL,
            Title     TEXT NOT NULL DEFAULT '',
            IsPinned  INTEGER NOT NULL DEFAULT 0,
            IsMuted   INTEGER NOT NULL DEFAULT 0,
            IsActive  INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (SessionId, Position)
        );

        CREATE TABLE IF NOT EXISTS ClosedTabs (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            Url       TEXT NOT NULL,
            Title     TEXT NOT NULL DEFAULT '',
            IsPinned  INTEGER NOT NULL DEFAULT 0,
            IsMuted   INTEGER NOT NULL DEFAULT 0,
            ClosedUtc INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Profiles (
            Id         TEXT PRIMARY KEY,
            Name       TEXT NOT NULL,
            ColorHex   TEXT NOT NULL DEFAULT '#7C3AED',
            CreatedUtc INTEGER NOT NULL,
            IsGuest    INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Themes (
            Id         TEXT PRIMARY KEY,
            Name       TEXT NOT NULL,
            Json       TEXT NOT NULL,
            IsBuiltIn  INTEGER NOT NULL DEFAULT 0,
            CreatedUtc INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Shortcuts (
            Id        TEXT PRIMARY KEY,
            Title     TEXT NOT NULL,
            Url       TEXT NOT NULL,
            IconUrl   TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0
        );
        """);

    private static readonly SchemaMigration V2 = new(2, "Site permissions and download hashes", """
        CREATE TABLE IF NOT EXISTS SitePermissions (
            Origin     TEXT NOT NULL,
            Kind       TEXT NOT NULL,
            Decision   TEXT NOT NULL DEFAULT 'Ask',
            UpdatedUtc INTEGER NOT NULL,
            PRIMARY KEY (Origin, Kind)
        );

        ALTER TABLE Downloads ADD COLUMN Sha256 TEXT NULL;
        """);
}
