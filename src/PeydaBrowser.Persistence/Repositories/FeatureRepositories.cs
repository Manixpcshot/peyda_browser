using Microsoft.Data.Sqlite;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Persistence.Repositories;

public sealed class SqliteBookmarkRepository(PeydaDatabase database) : IBookmarkRepository
{
    private const string Columns = "Id, FolderId, Title, Url, SortOrder, CreatedUtc, ModifiedUtc, FaviconUrl";

    private static Bookmark Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        FolderId = r.GetNullableString(1),
        Title = r.GetString(2),
        Url = r.GetString(3),
        SortOrder = r.GetInt32(4),
        CreatedUtc = SqliteTime.FromMs(r.GetInt64(5)),
        ModifiedUtc = SqliteTime.FromMs(r.GetInt64(6)),
        FaviconUrl = r.GetNullableString(7)
    };

    private async Task<IReadOnlyList<Bookmark>> QueryAsync(string sql, Action<SqliteCommand>? bind, CancellationToken ct)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        var list = new List<Bookmark>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public Task<IReadOnlyList<Bookmark>> GetAllAsync(CancellationToken ct = default) =>
        QueryAsync($"SELECT {Columns} FROM Bookmarks ORDER BY SortOrder, Title;", null, ct);

    public Task<IReadOnlyList<Bookmark>> GetByFolderAsync(string? folderId, CancellationToken ct = default) =>
        folderId is null
            ? QueryAsync($"SELECT {Columns} FROM Bookmarks WHERE FolderId IS NULL ORDER BY SortOrder, Title;", null, ct)
            : QueryAsync($"SELECT {Columns} FROM Bookmarks WHERE FolderId = $folder ORDER BY SortOrder, Title;",
                cmd => cmd.Parameters.AddWithValue("$folder", folderId), ct);

    public Task<IReadOnlyList<Bookmark>> SearchAsync(string query, int limit, CancellationToken ct = default) =>
        QueryAsync($"""
            SELECT {Columns} FROM Bookmarks
            WHERE Title LIKE $pattern ESCAPE '\' OR Url LIKE $pattern ESCAPE '\'
            ORDER BY Title LIMIT $limit;
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("$pattern", "%" + ReaderExtensions.EscapeLike(query) + "%");
            cmd.Parameters.AddWithValue("$limit", limit);
        }, ct);

    public async Task<Bookmark?> FindByUrlAsync(string url, CancellationToken ct = default)
    {
        var results = await QueryAsync($"SELECT {Columns} FROM Bookmarks WHERE Url = $url LIMIT 1;",
            cmd => cmd.Parameters.AddWithValue("$url", url), ct).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<Bookmark> AddAsync(Bookmark bookmark, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        // INSERT OR IGNORE + unique (folder, url) index prevents accidental duplicates.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO Bookmarks (Id, FolderId, Title, Url, SortOrder, CreatedUtc, ModifiedUtc, FaviconUrl)
                VALUES ($id, $folder, $title, $url, $sort, $created, $modified, $favicon);
                """;
            cmd.Parameters.AddWithValue("$id", bookmark.Id);
            cmd.Parameters.AddWithValue("$folder", (object?)bookmark.FolderId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$title", bookmark.Title);
            cmd.Parameters.AddWithValue("$url", bookmark.Url);
            cmd.Parameters.AddWithValue("$sort", bookmark.SortOrder);
            cmd.Parameters.AddWithValue("$created", SqliteTime.ToMs(bookmark.CreatedUtc));
            cmd.Parameters.AddWithValue("$modified", SqliteTime.ToMs(bookmark.ModifiedUtc));
            cmd.Parameters.AddWithValue("$favicon", (object?)bookmark.FaviconUrl ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using var select = conn.CreateCommand();
        select.CommandText = $"SELECT {Columns} FROM Bookmarks WHERE Url = $url AND COALESCE(FolderId, '') = COALESCE($folder, '') LIMIT 1;";
        select.Parameters.AddWithValue("$url", bookmark.Url);
        select.Parameters.AddWithValue("$folder", (object?)bookmark.FolderId ?? DBNull.Value);
        await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false)) return Read(reader);
        return bookmark;
    }

    public async Task UpdateAsync(Bookmark bookmark, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Bookmarks SET FolderId = $folder, Title = $title, Url = $url, SortOrder = $sort,
                ModifiedUtc = $modified, FaviconUrl = $favicon
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", bookmark.Id);
        cmd.Parameters.AddWithValue("$folder", (object?)bookmark.FolderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", bookmark.Title);
        cmd.Parameters.AddWithValue("$url", bookmark.Url);
        cmd.Parameters.AddWithValue("$sort", bookmark.SortOrder);
        cmd.Parameters.AddWithValue("$modified", SqliteTime.ToMs(bookmark.ModifiedUtc));
        cmd.Parameters.AddWithValue("$favicon", (object?)bookmark.FaviconUrl ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Bookmarks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BookmarkFolder>> GetFoldersAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ParentId, Name, SortOrder, CreatedUtc FROM BookmarkFolders ORDER BY SortOrder, Name;";
        var list = new List<BookmarkFolder>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new BookmarkFolder
            {
                Id = reader.GetString(0),
                ParentId = reader.GetNullableString(1),
                Name = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                CreatedUtc = SqliteTime.FromMs(reader.GetInt64(4))
            });
        }
        return list;
    }

    public async Task AddFolderAsync(BookmarkFolder folder, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO BookmarkFolders (Id, ParentId, Name, SortOrder, CreatedUtc) VALUES ($id, $parent, $name, $sort, $created);";
        cmd.Parameters.AddWithValue("$id", folder.Id);
        cmd.Parameters.AddWithValue("$parent", (object?)folder.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", folder.Name);
        cmd.Parameters.AddWithValue("$sort", folder.SortOrder);
        cmd.Parameters.AddWithValue("$created", SqliteTime.ToMs(folder.CreatedUtc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateFolderAsync(BookmarkFolder folder, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE BookmarkFolders SET ParentId = $parent, Name = $name, SortOrder = $sort WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", folder.Id);
        cmd.Parameters.AddWithValue("$parent", (object?)folder.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", folder.Name);
        cmd.Parameters.AddWithValue("$sort", folder.SortOrder);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteFolderAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM BookmarkFolders WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteDownloadRepository(PeydaDatabase database) : IDownloadRepository
{
    private const string Columns = "Id, FileName, SourceUrl, TargetPath, TotalBytes, ReceivedBytes, State, StartedUtc, CompletedUtc, Sha256";

    private static DownloadRecord Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        FileName = r.GetString(1),
        SourceUrl = r.GetString(2),
        TargetPath = r.GetString(3),
        TotalBytes = r.GetInt64(4),
        ReceivedBytes = r.GetInt64(5),
        State = Enum.TryParse<DownloadState>(r.GetString(6), out var state) ? state : DownloadState.Interrupted,
        StartedUtc = SqliteTime.FromMs(r.GetInt64(7)),
        CompletedUtc = r.IsDBNull(8) ? null : SqliteTime.FromMs(r.GetInt64(8)),
        Sha256 = r.GetNullableString(9)
    };

    public async Task<IReadOnlyList<DownloadRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM Downloads ORDER BY StartedUtc DESC;";
        var list = new List<DownloadRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public async Task UpsertAsync(DownloadRecord record, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Downloads (Id, FileName, SourceUrl, TargetPath, TotalBytes, ReceivedBytes, State, StartedUtc, CompletedUtc, Sha256)
            VALUES ($id, $name, $source, $target, $total, $received, $state, $started, $completed, $sha)
            ON CONFLICT(Id) DO UPDATE SET
                FileName = excluded.FileName, TargetPath = excluded.TargetPath, TotalBytes = excluded.TotalBytes,
                ReceivedBytes = excluded.ReceivedBytes, State = excluded.State, CompletedUtc = excluded.CompletedUtc,
                Sha256 = excluded.Sha256;
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$name", record.FileName);
        cmd.Parameters.AddWithValue("$source", record.SourceUrl);
        cmd.Parameters.AddWithValue("$target", record.TargetPath);
        cmd.Parameters.AddWithValue("$total", record.TotalBytes);
        cmd.Parameters.AddWithValue("$received", record.ReceivedBytes);
        cmd.Parameters.AddWithValue("$state", record.State.ToString());
        cmd.Parameters.AddWithValue("$started", SqliteTime.ToMs(record.StartedUtc));
        cmd.Parameters.AddWithValue("$completed", record.CompletedUtc is null ? DBNull.Value : SqliteTime.ToMs(record.CompletedUtc.Value));
        cmd.Parameters.AddWithValue("$sha", (object?)record.Sha256 ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Downloads WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearCompletedAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Downloads WHERE State IN ('Completed', 'Cancelled', 'Failed');";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteSessionRepository(PeydaDatabase database) : ISessionRepository
{
    public async Task SaveAutoSessionAsync(SessionSnapshot snapshot, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Execute(conn, tx, "DELETE FROM SessionTabs WHERE SessionId = 'auto';", null, ct).ConfigureAwait(false);
        await Execute(conn, tx,
            "INSERT INTO Sessions (Id, Name, CreatedUtc) VALUES ('auto', 'auto', $now) ON CONFLICT(Id) DO UPDATE SET CreatedUtc = excluded.CreatedUtc;",
            cmd => cmd.Parameters.AddWithValue("$now", SqliteTime.ToMs(snapshot.CreatedUtc)), ct).ConfigureAwait(false);
        for (var i = 0; i < snapshot.Tabs.Count; i++)
        {
            var tab = snapshot.Tabs[i];
            var position = i;
            await Execute(conn, tx, """
                INSERT INTO SessionTabs (SessionId, Position, Url, Title, IsPinned, IsMuted, IsActive)
                VALUES ('auto', $pos, $url, $title, $pinned, $muted, $active);
                """, cmd =>
            {
                cmd.Parameters.AddWithValue("$pos", position);
                cmd.Parameters.AddWithValue("$url", tab.Url);
                cmd.Parameters.AddWithValue("$title", tab.Title);
                cmd.Parameters.AddWithValue("$pinned", tab.IsPinned ? 1 : 0);
                cmd.Parameters.AddWithValue("$muted", tab.IsMuted ? 1 : 0);
                cmd.Parameters.AddWithValue("$active", tab.IsActive ? 1 : 0);
            }, ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<SessionSnapshot?> GetAutoSessionAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var head = conn.CreateCommand();
        head.CommandText = "SELECT CreatedUtc FROM Sessions WHERE Id = 'auto';";
        var created = await head.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (created is null or DBNull) return null;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Url, Title, IsPinned, IsMuted, IsActive FROM SessionTabs
            WHERE SessionId = 'auto' ORDER BY Position;
            """;
        var tabs = new List<SessionTab>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tabs.Add(new SessionTab
            {
                Url = reader.GetString(0),
                Title = reader.GetString(1),
                IsPinned = reader.GetInt64(2) != 0,
                IsMuted = reader.GetInt64(3) != 0,
                IsActive = reader.GetInt64(4) != 0
            });
        }
        if (tabs.Count == 0) return null;
        return new SessionSnapshot
        {
            Id = "auto",
            Name = "auto",
            CreatedUtc = SqliteTime.FromMs(Convert.ToInt64(created)),
            Tabs = tabs
        };
    }

    public async Task ClearAutoSessionAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await Execute(conn, null, "DELETE FROM Sessions WHERE Id = 'auto';", null, ct).ConfigureAwait(false);
    }

    public async Task PushClosedTabAsync(ClosedTab tab, int maxItems, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await Execute(conn, tx, """
            INSERT INTO ClosedTabs (Url, Title, IsPinned, IsMuted, ClosedUtc) VALUES ($url, $title, $pinned, $muted, $closed);
            """, cmd =>
        {
            cmd.Parameters.AddWithValue("$url", tab.Url);
            cmd.Parameters.AddWithValue("$title", tab.Title);
            cmd.Parameters.AddWithValue("$pinned", tab.IsPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$muted", tab.IsMuted ? 1 : 0);
            cmd.Parameters.AddWithValue("$closed", SqliteTime.ToMs(tab.ClosedUtc));
        }, ct).ConfigureAwait(false);
        await Execute(conn, tx, """
            DELETE FROM ClosedTabs WHERE Id NOT IN (SELECT Id FROM ClosedTabs ORDER BY ClosedUtc DESC, Id DESC LIMIT $max);
            """, cmd => cmd.Parameters.AddWithValue("$max", Math.Max(1, maxItems)), ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ClosedTab?> PopClosedTabAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        ClosedTab? result = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "SELECT Id, Url, Title, IsPinned, IsMuted, ClosedUtc FROM ClosedTabs ORDER BY ClosedUtc DESC, Id DESC LIMIT 1;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                result = new ClosedTab
                {
                    Id = reader.GetInt64(0),
                    Url = reader.GetString(1),
                    Title = reader.GetString(2),
                    IsPinned = reader.GetInt64(3) != 0,
                    IsMuted = reader.GetInt64(4) != 0,
                    ClosedUtc = SqliteTime.FromMs(reader.GetInt64(5))
                };
            }
        }
        if (result is not null)
        {
            var id = result.Id;
            await Execute(conn, tx, "DELETE FROM ClosedTabs WHERE Id = $id;",
                cmd => cmd.Parameters.AddWithValue("$id", id), ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<ClosedTab>> GetClosedTabsAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Url, Title, IsPinned, IsMuted, ClosedUtc FROM ClosedTabs ORDER BY ClosedUtc DESC, Id DESC;";
        var list = new List<ClosedTab>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ClosedTab
            {
                Id = reader.GetInt64(0),
                Url = reader.GetString(1),
                Title = reader.GetString(2),
                IsPinned = reader.GetInt64(3) != 0,
                IsMuted = reader.GetInt64(4) != 0,
                ClosedUtc = SqliteTime.FromMs(reader.GetInt64(5))
            });
        }
        return list;
    }

    private static async Task Execute(SqliteConnection conn, System.Data.Common.DbTransaction? tx, string sql, Action<SqliteCommand>? bind, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteProfileRepository(PeydaDatabase database) : IProfileRepository
{
    public async Task<BrowserProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, ColorHex, CreatedUtc, IsGuest FROM Profiles WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new BrowserProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            ColorHex = reader.GetString(2),
            CreatedUtc = SqliteTime.FromMs(reader.GetInt64(3)),
            IsGuest = reader.GetInt64(4) != 0
        };
    }

    public async Task UpsertAsync(BrowserProfile profile, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Profiles (Id, Name, ColorHex, CreatedUtc, IsGuest) VALUES ($id, $name, $color, $created, $guest)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, ColorHex = excluded.ColorHex;
            """;
        cmd.Parameters.AddWithValue("$id", profile.Id);
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$color", profile.ColorHex);
        cmd.Parameters.AddWithValue("$created", SqliteTime.ToMs(profile.CreatedUtc));
        cmd.Parameters.AddWithValue("$guest", profile.IsGuest ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteThemeRepository(PeydaDatabase database, IClock clock) : IThemeRepository
{
    public async Task<IReadOnlyList<(string Id, string Name, string Json, bool IsBuiltIn)>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Json, IsBuiltIn FROM Themes ORDER BY IsBuiltIn DESC, Name;";
        var list = new List<(string, string, string, bool)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0));
        return list;
    }

    public async Task UpsertAsync(string id, string name, string json, bool isBuiltIn, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Themes (Id, Name, Json, IsBuiltIn, CreatedUtc) VALUES ($id, $name, $json, $builtIn, $now)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, Json = excluded.Json;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$builtIn", isBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", SqliteTime.ToMs(clock.UtcNow));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Themes WHERE Id = $id AND IsBuiltIn = 0;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteShortcutRepository(PeydaDatabase database) : IShortcutRepository
{
    public async Task<IReadOnlyList<NewTabShortcut>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Url, IconUrl, SortOrder FROM Shortcuts ORDER BY SortOrder, Title;";
        var list = new List<NewTabShortcut>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new NewTabShortcut
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Url = reader.GetString(2),
                IconUrl = reader.GetNullableString(3),
                SortOrder = reader.GetInt32(4)
            });
        }
        return list;
    }

    public async Task UpsertAsync(NewTabShortcut shortcut, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Shortcuts (Id, Title, Url, IconUrl, SortOrder) VALUES ($id, $title, $url, $icon, $sort)
            ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, Url = excluded.Url, IconUrl = excluded.IconUrl, SortOrder = excluded.SortOrder;
            """;
        cmd.Parameters.AddWithValue("$id", shortcut.Id);
        cmd.Parameters.AddWithValue("$title", shortcut.Title);
        cmd.Parameters.AddWithValue("$url", shortcut.Url);
        cmd.Parameters.AddWithValue("$icon", (object?)shortcut.IconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", shortcut.SortOrder);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Shortcuts WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetSortOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE Shortcuts SET SortOrder = $sort WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$sort", i);
            cmd.Parameters.AddWithValue("$id", orderedIds[i]);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqlitePermissionRepository(PeydaDatabase database) : IPermissionRepository
{
    private static SitePermission Read(SqliteDataReader r) => new()
    {
        Origin = r.GetString(0),
        Kind = Enum.TryParse<BrowserPermissionKind>(r.GetString(1), out var kind) ? kind : BrowserPermissionKind.Other,
        Decision = Enum.TryParse<PermissionDecision>(r.GetString(2), out var decision) ? decision : PermissionDecision.Ask,
        UpdatedUtc = SqliteTime.FromMs(r.GetInt64(3))
    };

    public async Task<SitePermission?> GetAsync(string origin, BrowserPermissionKind kind, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Origin, Kind, Decision, UpdatedUtc FROM SitePermissions WHERE Origin = $origin AND Kind = $kind;";
        cmd.Parameters.AddWithValue("$origin", origin);
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<SitePermission>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Origin, Kind, Decision, UpdatedUtc FROM SitePermissions ORDER BY Origin, Kind;";
        var list = new List<SitePermission>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public async Task UpsertAsync(SitePermission permission, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SitePermissions (Origin, Kind, Decision, UpdatedUtc) VALUES ($origin, $kind, $decision, $now)
            ON CONFLICT(Origin, Kind) DO UPDATE SET Decision = excluded.Decision, UpdatedUtc = excluded.UpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("$origin", permission.Origin);
        cmd.Parameters.AddWithValue("$kind", permission.Kind.ToString());
        cmd.Parameters.AddWithValue("$decision", permission.Decision.ToString());
        cmd.Parameters.AddWithValue("$now", SqliteTime.ToMs(permission.UpdatedUtc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string origin, BrowserPermissionKind kind, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SitePermissions WHERE Origin = $origin AND Kind = $kind;";
        cmd.Parameters.AddWithValue("$origin", origin);
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAllForOriginAsync(string origin, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SitePermissions WHERE Origin = $origin;";
        cmd.Parameters.AddWithValue("$origin", origin);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
