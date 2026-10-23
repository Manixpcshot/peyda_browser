using Microsoft.Data.Sqlite;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Infrastructure.Files;
using PeydaBrowser.Infrastructure.Profiles;
using PeydaBrowser.Persistence;
using PeydaBrowser.Persistence.Repositories;
using PeydaBrowser.Theming;

namespace PeydaBrowser.IntegrationTests;

public class ProfileEndToEndTests : IDisposable
{
    private readonly string _root;
    private readonly ProfileManager _manager;

    public ProfileEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PeydaE2E", Guid.NewGuid().ToString("N"));
        _manager = new ProfileManager(new AppPaths(_root), new SystemClock());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static async Task<PeydaDatabase> OpenProfileAsync(ProfileContext ctx)
    {
        var db = new PeydaDatabase(new PeydaDatabaseOptions { DatabasePath = ctx.DatabasePath });
        await db.InitializeAsync();
        var clock = new SystemClock();
        var seeder = new DatabaseSeeder(
            new SqliteSearchEngineRepository(db), new SqliteThemeRepository(db, clock),
            new SqliteProfileRepository(db), new SqliteShortcutRepository(db), clock);
        await seeder.SeedAsync(ctx.Profile);
        return db;
    }

    [Fact]
    public async Task Profiles_HaveIsolatedHistoryAndBookmarks()
    {
        var defaultCtx = await _manager.GetOrCreateDefaultContextAsync();
        var second = await _manager.CreateProfileAsync("Work", "#2563EB");
        var secondCtx = _manager.ResolveContext(second, isPrivate: false, ephemeralDirectory: null);

        var db1 = await OpenProfileAsync(defaultCtx);
        var db2 = await OpenProfileAsync(secondCtx);
        Assert.NotEqual(db1.DatabasePath, db2.DatabasePath);

        var history1 = new HistoryService(new SqliteHistoryRepository(db1), new SystemClock());
        await history1.RecordVisitAsync("https://personal.example", "Personal", recordingEnabled: true, isPrivateContext: false);

        Assert.Empty(await new SqliteHistoryRepository(db2).GetRecentAsync(10));
        Assert.Single(await history1.GetRecentAsync());
    }

    [Fact]
    public async Task PrivateContext_LeavesNoTraceAfterDisposal()
    {
        var ctx = _manager.CreatePrivateContext();
        Assert.True(ctx.IsPrivate);
        Assert.True(ctx.Profile.IsEphemeral);

        var db = await OpenProfileAsync(ctx);
        var history = new HistoryService(new SqliteHistoryRepository(db), new SystemClock());
        await history.RecordVisitAsync("https://secret.example", "Secret", recordingEnabled: true, isPrivateContext: true);
        Assert.Empty(await history.GetRecentAsync());

        var dir = ctx.ProfileDirectory;
        SqliteConnection.ClearAllPools();
        _manager.DisposeEphemeralContext(ctx);
        var dbLeftBehind = Directory.Exists(dir) &&
                            Directory.EnumerateFiles(dir, "*.db", SearchOption.AllDirectories).Any();
        Assert.False(dbLeftBehind);
    }

    [Fact]
    public async Task BookmarkExport_FromOneProfile_ImportsIntoAnother()
    {
        var ctxA = await _manager.GetOrCreateDefaultContextAsync();
        var descriptorB = await _manager.CreateProfileAsync("Other", "#16A34A");
        var ctxB = _manager.ResolveContext(descriptorB, isPrivate: false, ephemeralDirectory: null);

        var dbA = await OpenProfileAsync(ctxA);
        var dbB = await OpenProfileAsync(ctxB);
        var bookmarksA = new SqliteBookmarkRepository(dbA);
        var bookmarksB = new SqliteBookmarkRepository(dbB);
        var now = DateTimeOffset.UtcNow;

        await bookmarksA.AddFolderAsync(new BookmarkFolder { Id = "f1", Name = "Reading", CreatedUtc = now });
        await bookmarksA.AddAsync(new Bookmark
        {
            Id = "b1", FolderId = "f1", Title = "Peyda repo", Url = "https://github.com/Manixpcshot/peyda_browser",
            CreatedUtc = now, ModifiedUtc = now
        });

        var html = BookmarkHtmlService.Export(await bookmarksA.GetAllAsync(), await bookmarksA.GetFoldersAsync());
        var imported = BookmarkHtmlService.Import(html);
        Assert.Single(imported.Bookmarks);
        Assert.Equal("Reading", imported.Bookmarks[0].FolderPath.Single());

        await bookmarksB.AddFolderAsync(new BookmarkFolder { Id = "rf", Name = imported.Bookmarks[0].FolderPath[0], CreatedUtc = now });
        await bookmarksB.AddAsync(new Bookmark
        {
            Id = "nb", FolderId = "rf", Title = imported.Bookmarks[0].Title, Url = imported.Bookmarks[0].Url,
            CreatedUtc = now, ModifiedUtc = now
        });
        Assert.Single(await bookmarksB.GetByFolderAsync("rf"));
    }

    [Fact]
    public async Task ThemeExportImport_RoundTripsThroughFile()
    {
        var service = new ThemeFileService();
        var path = Path.Combine(_root, "custom.peydatheme.json");
        Directory.CreateDirectory(_root);
        var custom = BuiltInThemes.PeydaMidnight.Definition.Clone();
        custom.Name = "My Midnight";
        custom.Accent = "#FF0066";

        await service.ExportAsync(custom, path);
        var result = await service.ImportAsync(path);

        Assert.True(result.IsValid, string.Join(",", result.Errors));
        Assert.Equal("#FF0066", result.Theme.Accent);
        Assert.Equal("My Midnight", result.Theme.Name);
    }

    [Fact]
    public async Task CorruptThemeFile_FallsBackSafely()
    {
        var path = Path.Combine(_root, "broken.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "not a theme at all {{{");
        var result = await new ThemeFileService().ImportAsync(path);
        Assert.False(result.IsValid);
        Assert.Equal("Peyda Dark", result.Theme.Name);
    }

    [Fact]
    public async Task ClearHistory_WithRanges()
    {
        var ctx = await _manager.GetOrCreateDefaultContextAsync();
        var db = await OpenProfileAsync(ctx);
        var repo = new SqliteHistoryRepository(db);
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var service = new HistoryService(repo, new FixedClock(now));

        await repo.UpsertVisitAsync("https://recent.example", "r", now.AddMinutes(-30));
        await repo.UpsertVisitAsync("https://week.example", "w", now.AddDays(-3));
        await repo.UpsertVisitAsync("https://month.example", "m", now.AddDays(-40));

        await service.ClearAsync(ClearTimeRange.Last7Days);
        var remaining = await repo.GetRecentAsync(10);
        Assert.Single(remaining);
        Assert.Equal("https://month.example", remaining[0].Url);

        await service.ClearAsync(ClearTimeRange.AllTime);
        Assert.Empty(await repo.GetRecentAsync(10));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
