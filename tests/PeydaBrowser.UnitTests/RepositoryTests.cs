using Microsoft.Data.Sqlite;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Persistence;
using PeydaBrowser.Persistence.Repositories;

namespace PeydaBrowser.UnitTests;

internal sealed class TestDatabase : IDisposable
{
    public PeydaDatabase Database { get; }
    public string DirectoryPath { get; }

    private TestDatabase(PeydaDatabase database, string directory)
    {
        Database = database;
        DirectoryPath = directory;
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PeydaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = new PeydaDatabase(new PeydaDatabaseOptions { DatabasePath = Path.Combine(dir, "test.db") });
        await db.InitializeAsync();
        return new TestDatabase(db, dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch (IOException) { }
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

public class HistoryRepositoryTests
{
    [Fact]
    public async Task UpsertVisit_IncrementsCountAndKeepsLatestTitle()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteHistoryRepository(testDb.Database);
        var t1 = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

        await repo.UpsertVisitAsync("https://example.com", "Old title", t1);
        await repo.UpsertVisitAsync("https://example.com", "New title", t1.AddHours(1));
        await repo.UpsertVisitAsync("https://example.com", string.Empty, t1.AddHours(2));

        var entries = await repo.GetRecentAsync(10);
        Assert.Single(entries);
        Assert.Equal(3, entries[0].VisitCount);
        Assert.Equal("New title", entries[0].Title);
        Assert.Equal(t1.AddHours(2), entries[0].LastVisitUtc);
    }

    [Fact]
    public async Task Search_EscapesLikeWildcards()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteHistoryRepository(testDb.Database);
        var now = DateTimeOffset.UtcNow;
        await repo.UpsertVisitAsync("https://example.com/100%_done", "percent", now);
        await repo.UpsertVisitAsync("https://other.example", "other", now);

        var found = await repo.SearchAsync("100%_done", 10);
        Assert.Single(found);
        Assert.Contains("100%_done", found[0].Url);
    }

    [Fact]
    public async Task DeleteRange_RemovesOnlyRecentEntries()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteHistoryRepository(testDb.Database);
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        await repo.UpsertVisitAsync("https://new.example", "new", now.AddHours(-1));
        await repo.UpsertVisitAsync("https://old.example", "old", now.AddDays(-10));

        await repo.DeleteRangeAsync(now.AddHours(-24));

        var remaining = await repo.GetRecentAsync(10);
        Assert.Single(remaining);
        Assert.Equal("https://old.example", remaining[0].Url);
    }

    [Fact]
    public async Task SqlInjectionAttempt_IsTreatedAsData()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteHistoryRepository(testDb.Database);
        var now = DateTimeOffset.UtcNow;
        await repo.UpsertVisitAsync("https://safe.example", "safe", now);

        var found = await repo.SearchAsync("'; DROP TABLE History; --", 10);
        Assert.Empty(found);
        Assert.Single(await repo.GetRecentAsync(10));
    }
}

public class BookmarkRepositoryTests
{
    [Fact]
    public async Task Add_DuplicateUrlInSameFolder_ReturnsExisting()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteBookmarkRepository(testDb.Database);
        var now = DateTimeOffset.UtcNow;
        var first = await repo.AddAsync(new Bookmark
        {
            Id = "b1", Title = "Example", Url = "https://example.com", CreatedUtc = now, ModifiedUtc = now
        });
        var second = await repo.AddAsync(new Bookmark
        {
            Id = "b2", Title = "Example again", Url = "https://example.com", CreatedUtc = now, ModifiedUtc = now
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task Folders_CascadeDeleteBookmarks()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteBookmarkRepository(testDb.Database);
        var now = DateTimeOffset.UtcNow;
        await repo.AddFolderAsync(new BookmarkFolder { Id = "f1", Name = "Folder", CreatedUtc = now });
        await repo.AddAsync(new Bookmark
        {
            Id = "b1", FolderId = "f1", Title = "In folder", Url = "https://a.example", CreatedUtc = now, ModifiedUtc = now
        });

        await repo.DeleteFolderAsync("f1");

        Assert.Empty(await repo.GetAllAsync());
        Assert.Empty(await repo.GetFoldersAsync());
    }
}

public class SessionRepositoryTests
{
    [Fact]
    public async Task AutoSession_RoundTrips_AndClosedTabsStackWorks()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqliteSessionRepository(testDb.Database);
        var now = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
        var service = new SessionService(repo, new FixedClock(now));

        await service.SaveAsync(new[]
        {
            new SessionTab { Url = "https://a.example", Title = "A" },
            new SessionTab { Url = "peyda://newtab", Title = "New Tab", IsActive = true },
            new SessionTab { Url = "about:blank", Title = "blank" }
        }, isPrivateContext: false);

        var loaded = await service.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Tabs.Count);
        Assert.Equal("https://a.example", loaded.Tabs[0].Url);
        Assert.True(loaded.Tabs[1].IsActive);

        await service.SaveAsync(new[] { new SessionTab { Url = "https://secret.example" } }, isPrivateContext: true);
        loaded = await service.LoadAsync();
        Assert.Equal(2, loaded!.Tabs.Count);

        await service.PushClosedTabAsync(new SessionTab { Url = "https://closed.example", Title = "Closed" }, isPrivateContext: false);
        var popped = await service.PopClosedTabAsync();
        Assert.NotNull(popped);
        Assert.Equal("https://closed.example", popped!.Url);
        Assert.Null(await service.PopClosedTabAsync());
    }
}

public class SettingsAndPermissionRepositoryTests
{
    [Fact]
    public async Task Settings_UpsertAndReadBack()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var service = new SettingsService(new SqliteSettingsRepository(testDb.Database));
        var settings = new BrowserSettings { Language = "fa", ThemeId = "peyda-midnight" };
        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();
        Assert.Equal("fa", loaded.Language);
        Assert.Equal("peyda-midnight", loaded.ThemeId);
    }

    [Fact]
    public async Task Permissions_AreScopedPerOriginAndKind()
    {
        using var testDb = await TestDatabase.CreateAsync();
        var repo = new SqlitePermissionRepository(testDb.Database);
        var now = DateTimeOffset.UtcNow;
        await repo.UpsertAsync(new SitePermission
        {
            Origin = "https://meet.example", Kind = BrowserPermissionKind.Camera,
            Decision = PermissionDecision.Allow, UpdatedUtc = now
        });
        await repo.UpsertAsync(new SitePermission
        {
            Origin = "https://meet.example", Kind = BrowserPermissionKind.Notifications,
            Decision = PermissionDecision.Deny, UpdatedUtc = now
        });

        var camera = await repo.GetAsync("https://meet.example", BrowserPermissionKind.Camera);
        Assert.Equal(PermissionDecision.Allow, camera!.Decision);
        Assert.Null(await repo.GetAsync("https://other.example", BrowserPermissionKind.Camera));

        await repo.DeleteAllForOriginAsync("https://meet.example");
        Assert.Empty(await repo.GetAllAsync());
    }
}
