using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Theming;

namespace PeydaBrowser.UnitTests;

internal sealed class InMemorySettingsRepository : ISettingsRepository
{
    public Dictionary<string, string> Values { get; } = new();
    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Values.GetValueOrDefault(key));
    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Values));
    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }
}

public class SettingsServiceTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var repo = new InMemorySettingsRepository();
        var service = new SettingsService(repo);
        var settings = new BrowserSettings
        {
            Language = "fa",
            ThemeMode = ThemeModeSetting.Dark,
            DefaultSearchEngineId = SearchEngine.BuiltIn.BraveId,
            RecordHistory = false,
            StartupBehavior = StartupBehavior.OpenNewTabPage,
            NewTabGreetingName = "مانی"
        };

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal("fa", loaded.Language);
        Assert.Equal(ThemeModeSetting.Dark, loaded.ThemeMode);
        Assert.Equal(SearchEngine.BuiltIn.BraveId, loaded.DefaultSearchEngineId);
        Assert.False(loaded.RecordHistory);
        Assert.Equal(StartupBehavior.OpenNewTabPage, loaded.StartupBehavior);
        Assert.Equal("مانی", loaded.NewTabGreetingName);
    }

    [Fact]
    public async Task CorruptStoredValues_FallBackToDefaults()
    {
        var repo = new InMemorySettingsRepository();
        repo.Values["appearance.language"] = "xx";
        repo.Values["appearance.themeMode"] = "Banana";
        repo.Values["general.homePage"] = "javascript:alert(1)";
        repo.Values["privacy.recordHistory"] = "not-a-bool";
        var service = new SettingsService(repo);

        var loaded = await service.LoadAsync();

        Assert.Equal("en", loaded.Language);
        Assert.Equal(ThemeModeSetting.System, loaded.ThemeMode);
        Assert.Equal(PeydaUrl.NewTab, loaded.HomePage);
        Assert.True(loaded.RecordHistory);
        Assert.False(loaded.TelemetryEnabled, "Telemetry must default to off.");
    }
}

public class ThemeTests
{
    private const string SpecSampleJson = """
    {
      "name": "Peyda Midnight",
      "mode": "dark",
      "accent": "#7C3AED",
      "background": "#0F1117",
      "surface": "#171A23",
      "toolbar": "#151821",
      "textPrimary": "#F8FAFC",
      "textSecondary": "#94A3B8",
      "tabStyle": "rounded",
      "tabPosition": "top",
      "density": "normal",
      "sidebarEnabled": true,
      "transparency": 0.95
    }
    """;

    [Fact]
    public void SpecSampleJson_IsValid()
    {
        var result = ThemeSerializer.FromJson(SpecSampleJson);
        Assert.True(result.IsValid, string.Join(",", result.Errors));
        Assert.Equal("Peyda Midnight", result.Theme.Name);
        Assert.Equal("#7C3AED", result.Theme.Accent);
        Assert.Equal(0.95, result.Theme.Transparency);
    }

    [Fact]
    public void BrokenJson_ReturnsDefaultThemeWithoutThrowing()
    {
        var result = ThemeSerializer.FromJson("{ this is not json !!!");
        Assert.False(result.IsValid);
        Assert.Equal("Peyda Dark", result.Theme.Name);
    }

    [Fact]
    public void InvalidColorAndTransparency_AreSanitized()
    {
        var result = ThemeSerializer.FromJson("""{ "name": "Bad", "mode": "dark", "accent": "red; evil", "transparency": 42 }""");
        Assert.False(result.IsValid);
        Assert.Equal("#7C3AED", result.Theme.Accent);
        Assert.Equal(1.0, result.Theme.Transparency);
    }

    [Fact]
    public void JsonRoundTrip_PreservesCustomTheme()
    {
        var custom = BuiltInThemes.PeydaLight.Definition.Clone();
        custom.Name = "My Theme";
        custom.Accent = "#112233";
        var json = ThemeSerializer.ToJson(custom);
        var back = ThemeSerializer.FromJson(json);
        Assert.True(back.IsValid, string.Join(",", back.Errors));
        Assert.Equal("My Theme", back.Theme.Name);
        Assert.Equal("#112233", back.Theme.Accent);
        Assert.Equal("light", back.Theme.Mode);
    }

    [Fact]
    public void OversizedInput_IsRejected()
    {
        var huge = new string('x', 70 * 1024);
        var result = ThemeSerializer.FromJson(huge);
        Assert.False(result.IsValid);
        Assert.Contains("theme.json.tooLarge", result.Errors);
    }

    [Fact]
    public void GetByIdOrDefault_UnknownId_ReturnsDark()
    {
        Assert.Equal(BuiltInThemes.DarkId, BuiltInThemes.GetByIdOrDefault("nope").Id);
        Assert.Equal(BuiltInThemes.LightId, BuiltInThemes.GetByIdOrDefault("peyda-light").Id);
    }
}
