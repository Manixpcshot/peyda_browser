using Microsoft.Data.Sqlite;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Persistence.Repositories;

public sealed class SqliteSettingsRepository(PeydaDatabase database) : ISettingsRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings;";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values[reader.GetString(0)] = reader.GetString(1);
        return values;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Settings (Key, Value, UpdatedUtc) VALUES ($key, $value, $now)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedUtc = excluded.UpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$now", SqliteTime.ToMs(DateTimeOffset.UtcNow));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Settings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteSearchEngineRepository(PeydaDatabase database) : ISearchEngineRepository
{
    private const string Columns = "Id, Name, UrlTemplate, Bang, IconUrl, SortOrder, IsBuiltIn, CreatedUtc";

    private static SearchEngine Read(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        UrlTemplate = r.GetString(2),
        Bang = r.GetNullableString(3),
        IconUrl = r.GetNullableString(4),
        SortOrder = r.GetInt32(5),
        IsBuiltIn = r.GetInt64(6) != 0,
        CreatedUtc = SqliteTime.FromMs(r.GetInt64(7))
    };

    public async Task<IReadOnlyList<SearchEngine>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM SearchEngines ORDER BY SortOrder, Name;";
        var list = new List<SearchEngine>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public async Task<SearchEngine?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM SearchEngines WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task UpsertAsync(SearchEngine engine, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SearchEngines (Id, Name, UrlTemplate, Bang, IconUrl, SortOrder, IsBuiltIn, CreatedUtc)
            VALUES ($id, $name, $template, $bang, $icon, $sort, $builtIn, $created)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name, UrlTemplate = excluded.UrlTemplate, Bang = excluded.Bang,
                IconUrl = excluded.IconUrl, SortOrder = excluded.SortOrder;
            """;
        cmd.Parameters.AddWithValue("$id", engine.Id);
        cmd.Parameters.AddWithValue("$name", engine.Name);
        cmd.Parameters.AddWithValue("$template", engine.UrlTemplate);
        cmd.Parameters.AddWithValue("$bang", (object?)engine.Bang ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$icon", (object?)engine.IconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", engine.SortOrder);
        cmd.Parameters.AddWithValue("$builtIn", engine.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", SqliteTime.ToMs(engine.CreatedUtc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SearchEngines WHERE Id = $id;";
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
            cmd.CommandText = "UPDATE SearchEngines SET SortOrder = $sort WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$sort", i);
            cmd.Parameters.AddWithValue("$id", orderedIds[i]);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteHistoryRepository(PeydaDatabase database) : IHistoryRepository
{
    private const string Columns = "Id, Url, Title, VisitCount, FirstVisitUtc, LastVisitUtc, FaviconUrl";

    private static HistoryEntry Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Url = r.GetString(1),
        Title = r.GetString(2),
        VisitCount = r.GetInt32(3),
        FirstVisitUtc = SqliteTime.FromMs(r.GetInt64(4)),
        LastVisitUtc = SqliteTime.FromMs(r.GetInt64(5)),
        FaviconUrl = r.GetNullableString(6)
    };

    public async Task UpsertVisitAsync(string url, string title, DateTimeOffset visitedUtc, string? faviconUrl = null, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO History (Url, Title, VisitCount, FirstVisitUtc, LastVisitUtc, FaviconUrl)
            VALUES ($url, $title, 1, $now, $now, $favicon)
            ON CONFLICT(Url) DO UPDATE SET
                Title = CASE WHEN excluded.Title <> '' THEN excluded.Title ELSE History.Title END,
                VisitCount = History.VisitCount + 1,
                LastVisitUtc = excluded.LastVisitUtc,
                FaviconUrl = COALESCE(excluded.FaviconUrl, History.FaviconUrl);
            """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", title ?? string.Empty);
        cmd.Parameters.AddWithValue("$now", SqliteTime.ToMs(visitedUtc));
        cmd.Parameters.AddWithValue("$favicon", (object?)faviconUrl ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM History ORDER BY LastVisitUtc DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<HistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columns} FROM History
            WHERE Title LIKE $pattern ESCAPE '\' OR Url LIKE $pattern ESCAPE '\'
            ORDER BY VisitCount DESC, LastVisitUtc DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$pattern", "%" + ReaderExtensions.EscapeLike(query) + "%");
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<HistoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(reader));
        return list;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM History WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteRangeAsync(DateTimeOffset? fromUtc, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        if (fromUtc is null)
        {
            cmd.CommandText = "DELETE FROM History;";
        }
        else
        {
            cmd.CommandText = "DELETE FROM History WHERE LastVisitUtc >= $from;";
            cmd.Parameters.AddWithValue("$from", SqliteTime.ToMs(fromUtc.Value));
        }
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteByUrlAsync(string url, CancellationToken ct = default)
    {
        await using var conn = await database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM History WHERE Url = $url;";
        cmd.Parameters.AddWithValue("$url", url);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
