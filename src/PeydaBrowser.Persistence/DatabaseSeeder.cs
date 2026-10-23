using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Theming;

namespace PeydaBrowser.Persistence;

/// <summary>Idempotent first-run seeding: built-in search engines, themes,
/// the profile's own row and default New Tab shortcuts.</summary>
public sealed class DatabaseSeeder
{
    private readonly ISearchEngineRepository _searchEngines;
    private readonly IThemeRepository _themes;
    private readonly IProfileRepository _profiles;
    private readonly IShortcutRepository _shortcuts;
    private readonly IClock _clock;

    public DatabaseSeeder(
        ISearchEngineRepository searchEngines,
        IThemeRepository themes,
        IProfileRepository profiles,
        IShortcutRepository shortcuts,
        IClock clock)
    {
        _searchEngines = searchEngines;
        _themes = themes;
        _profiles = profiles;
        _shortcuts = shortcuts;
        _clock = clock;
    }

    public async Task SeedAsync(BrowserProfile profile, CancellationToken ct = default)
    {
        if ((await _searchEngines.GetAllAsync(ct).ConfigureAwait(false)).Count == 0)
        {
            foreach (var engine in SearchEngine.BuiltIn.CreateDefaults(_clock.UtcNow))
                await _searchEngines.UpsertAsync(engine, ct).ConfigureAwait(false);
        }

        var existingThemes = await _themes.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var entry in BuiltInThemes.All)
        {
            if (existingThemes.All(t => t.Id != entry.Id))
                await _themes.UpsertAsync(entry.Id, entry.Definition.Name, ThemeSerializer.ToJson(entry.Definition), isBuiltIn: true, ct).ConfigureAwait(false);
        }

        await _profiles.UpsertAsync(profile, ct).ConfigureAwait(false);

        if ((await _shortcuts.GetAllAsync(ct).ConfigureAwait(false)).Count == 0)
        {
            var defaults = new[]
            {
                ("Google", "https://www.google.com"),
                ("Wikipedia", "https://www.wikipedia.org"),
                ("GitHub", "https://github.com"),
                ("YouTube", "https://www.youtube.com")
            };
            for (var i = 0; i < defaults.Length; i++)
            {
                var (title, url) = defaults[i];
                await _shortcuts.UpsertAsync(new NewTabShortcut
                {
                    Id = "shortcut-" + Guid.NewGuid().ToString("N")[..12],
                    Title = title,
                    Url = url,
                    IconUrl = url.TrimEnd('/') + "/favicon.ico",
                    SortOrder = i
                }, ct).ConfigureAwait(false);
            }
        }
    }
}
