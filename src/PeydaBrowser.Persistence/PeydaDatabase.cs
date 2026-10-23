using Microsoft.Data.Sqlite;
using PeydaBrowser.Persistence.Migrations;

namespace PeydaBrowser.Persistence;

public sealed class PeydaDatabaseOptions
{
    public required string DatabasePath { get; init; }

    /// <summary>Overridable for migration tests; defaults to all shipped migrations.</summary>
    public IReadOnlyList<SchemaMigration> Migrations { get; init; } = SchemaMigrations.All;
}

/// <summary>
/// Owns one profile's SQLite database: opens connections with safe pragmas and
/// applies pending schema migrations transactionally. All data access goes
/// through the typed repositories; no SQL is built from user input anywhere.
/// </summary>
public sealed class PeydaDatabase
{
    private readonly PeydaDatabaseOptions _options;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PeydaDatabase(PeydaDatabaseOptions options)
    {
        _options = options;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath => _options.DatabasePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            var directory = Path.GetDirectoryName(Path.GetFullPath(_options.DatabasePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            await using var connection = await OpenRawAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
            await MigrateAsync(connection, ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Opens a new connection. The database must be initialized first.</summary>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        if (!_initialized) await InitializeAsync(ct).ConfigureAwait(false);
        return await OpenRawAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenRawAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(value);
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;", ct).ConfigureAwait(false);
        return connection;
    }

    private async Task MigrateAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var current = Convert.ToInt32(await versionCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        foreach (var migration in _options.Migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = migration.Sql + $"\nPRAGMA user_version = {migration.Version};";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

internal static class SqliteTime
{
    public static long ToMs(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();
    public static DateTimeOffset FromMs(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
}

internal static class ReaderExtensions
{
    public static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
