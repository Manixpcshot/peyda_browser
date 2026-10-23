using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Persistence;
using PeydaBrowser.Persistence.Migrations;
using PeydaBrowser.Persistence.Repositories;

namespace PeydaBrowser.IntegrationTests;

public class MigrationTests : IDisposable
{
    private readonly string _dir;

    public MigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "PeydaMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Database_CreatedAtV1_UpgradesToLatest_KeepingData()
    {
        var path = Path.Combine(_dir, "profile.db");

        var v1Db = new PeydaDatabase(new PeydaDatabaseOptions
        {
            DatabasePath = path,
            Migrations = SchemaMigrations.All.Take(1).ToList()
        });
        await v1Db.InitializeAsync();
        Assert.Equal(1, await v1Db.GetSchemaVersionAsync());
        var history = new SqliteHistoryRepository(v1Db);
        await history.UpsertVisitAsync("https://before-upgrade.example", "Before", DateTimeOffset.UtcNow);
        SqliteConnection.ClearAllPools();

        var latestDb = new PeydaDatabase(new PeydaDatabaseOptions { DatabasePath = path });
        await latestDb.InitializeAsync();
        Assert.Equal(SchemaMigrations.LatestVersion, await latestDb.GetSchemaVersionAsync());

        var entries = await new SqliteHistoryRepository(latestDb).GetRecentAsync(10);
        Assert.Single(entries);
        Assert.Equal("https://before-upgrade.example", entries[0].Url);

        var permissions = new SqlitePermissionRepository(latestDb);
        await permissions.UpsertAsync(new SitePermission
        {
            Origin = "https://before-upgrade.example",
            Kind = BrowserPermissionKind.Camera,
            Decision = PermissionDecision.Deny,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
        Assert.Single(await permissions.GetAllAsync());
    }

    [Fact]
    public async Task FreshDatabase_ReachesLatestSchema_AndSeedsDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddPeydaPersistence(Path.Combine(_dir, "fresh.db"));
        await using var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<PeydaDatabase>();
        await db.InitializeAsync();
        Assert.Equal(SchemaMigrations.LatestVersion, await db.GetSchemaVersionAsync());

        var seeder = provider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(new BrowserProfile
        {
            Id = BrowserProfile.DefaultProfileId, Name = "Default", CreatedUtc = DateTimeOffset.UtcNow
        });

        var engines = await provider.GetRequiredService<ISearchEngineRepository>().GetAllAsync();
        Assert.Equal(new[] { "google", "brave", "duckduckgo" }, engines.Select(e => e.Id).ToArray());

        var themes = await provider.GetRequiredService<IThemeRepository>().GetAllAsync();
        Assert.Contains(themes, t => t.Id == "peyda-midnight" && t.IsBuiltIn);

        var shortcuts = await provider.GetRequiredService<IShortcutRepository>().GetAllAsync();
        Assert.Equal(4, shortcuts.Count);
    }
}
