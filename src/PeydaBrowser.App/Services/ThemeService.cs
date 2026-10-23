using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Theming;
using Windows.UI;

namespace PeydaBrowser.App.Services;

/// <summary>
/// Loads themes (built-in + user-imported from the database) and applies the
/// active one by installing a ResourceDictionary of Peyda brushes/sizes and
/// system accent overrides. XAML references these via ThemeResource so a theme
/// switch restyles the running window without a restart.
/// </summary>
public sealed class ThemeService
{
    private const int ThemeDictionaryIndex = 1;
    private readonly IThemeRepository _repository;
    private readonly List<ThemeCatalogEntry> _customThemes = new();

    public ThemeService(IThemeRepository repository) => _repository = repository;

    public ThemeCatalogEntry Current { get; private set; } = BuiltInThemes.PeydaDark;
    public bool IsDarkEffective { get; private set; } = true;

    public event EventHandler? ThemeChanged;

    public IReadOnlyList<ThemeCatalogEntry> AllThemes => BuiltInThemes.All.Concat(_customThemes).ToList();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _customThemes.Clear();
        foreach (var row in await _repository.GetAllAsync(ct).ConfigureAwait(true))
        {
            if (row.IsBuiltIn) continue;
            var parsed = ThemeSerializer.FromJson(row.Json);
            _customThemes.Add(new ThemeCatalogEntry(row.Id, IsBuiltIn: false, parsed.Theme));
        }
    }

    public async Task<string> SaveImportedThemeAsync(ThemeDefinition theme, CancellationToken ct = default)
    {
        var id = "theme-" + Guid.NewGuid().ToString("N")[..12];
        await _repository.UpsertAsync(id, theme.Name, ThemeSerializer.ToJson(theme), isBuiltIn: false, ct).ConfigureAwait(true);
        _customThemes.Add(new ThemeCatalogEntry(id, false, theme));
        return id;
    }

    public async Task DeleteCustomThemeAsync(string id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(true);
        _customThemes.RemoveAll(t => t.Id == id);
    }

    /// <summary>Applies the theme identified by settings (plus mode/density from settings).</summary>
    public void Apply(BrowserSettings settings, FrameworkElement? rootElement)
    {
        var entry = BuiltInThemes.GetByIdOrDefault(settings.ThemeId, _customThemes);
        var validated = ThemeValidator.Validate(entry.Definition).Theme;
        entry = entry with { Definition = validated };
        Current = entry;

        var systemDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        // Predictable rule: the explicit mode in Settings wins; selecting a theme
        // in the Settings page also updates the mode to match that theme.
        IsDarkEffective = settings.ThemeMode switch
        {
            ThemeModeSetting.Light => false,
            ThemeModeSetting.Dark => true,
            _ => systemDark
        };

        var t = validated;
        var dict = new ResourceDictionary
        {
            ["PeydaBackgroundBrush"] = Brush(t.Background, t.Transparency),
            ["PeydaSurfaceBrush"] = Brush(t.Surface, 1),
            ["PeydaToolbarBrush"] = Brush(t.Toolbar, t.Transparency),
            ["PeydaAccentBrush"] = Brush(t.Accent, 1),
            ["PeydaTextPrimaryBrush"] = Brush(t.TextPrimary, 1),
            ["PeydaTextSecondaryBrush"] = Brush(t.TextSecondary, 1),
            ["PeydaBorderBrush"] = Brush(t.Border, 1),
            ["PeydaTabActiveBrush"] = Brush(t.TabActive, 1),
            ["PeydaTabInactiveBrush"] = Brush(t.TabInactive, t.Transparency),
            ["PeydaToolbarHeight"] = ToolbarHeightFor(settings, t),
            ["PeydaControlCornerRadius"] = new CornerRadius(t.CornerRadius),
            ["ControlCornerRadius"] = new CornerRadius(t.CornerRadius),
            ["OverlayCornerRadius"] = new CornerRadius(Math.Max(4, t.CornerRadius * 1.5))
        };

        var accent = HexToColor(t.Accent);
        dict["SystemAccentColor"] = accent;
        dict["SystemAccentColorDark1"] = Shade(accent, -0.15);
        dict["SystemAccentColorDark2"] = Shade(accent, -0.30);
        dict["SystemAccentColorDark3"] = Shade(accent, -0.45);
        dict["SystemAccentColorLight1"] = Shade(accent, 0.15);
        dict["SystemAccentColorLight2"] = Shade(accent, 0.30);
        dict["SystemAccentColorLight3"] = Shade(accent, 0.45);

        var merged = Application.Current.Resources.MergedDictionaries;
        while (merged.Count <= ThemeDictionaryIndex) merged.Add(new ResourceDictionary());
        merged[ThemeDictionaryIndex] = dict;

        if (rootElement is not null)
            rootElement.RequestedTheme = IsDarkEffective ? ElementTheme.Dark : ElementTheme.Light;

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double ToolbarHeightFor(BrowserSettings settings, ThemeDefinition t) => settings.Density switch
    {
        UiDensity.Compact => Math.Min(t.ToolbarHeight, 40),
        UiDensity.Comfortable => Math.Max(t.ToolbarHeight, 56),
        _ => t.ToolbarHeight
    };

    private static SolidColorBrush Brush(string hex, double opacity) => new(HexToColor(hex)) { Opacity = Math.Clamp(opacity, 0.5, 1) };

    public static Color HexToColor(string hex)
    {
        var value = (hex ?? "#000000").Trim().TrimStart('#');
        try
        {
            if (value.Length == 6)
            {
                return Color.FromArgb(255,
                    Convert.ToByte(value[..2], 16),
                    Convert.ToByte(value[2..4], 16),
                    Convert.ToByte(value[4..6], 16));
            }
            if (value.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(value[..2], 16),
                    Convert.ToByte(value[2..4], 16),
                    Convert.ToByte(value[4..6], 16),
                    Convert.ToByte(value[6..8], 16));
            }
        }
        catch (FormatException)
        {
            // Themes are pre-validated; fall through to a visible default.
        }
        return Colors.MediumPurple;
    }

    private static Color Shade(Color color, double amount)
    {
        byte Adjust(byte channel) => amount >= 0
            ? (byte)Math.Min(255, channel + (255 - channel) * amount)
            : (byte)Math.Max(0, channel * (1 + amount));
        return Color.FromArgb(color.A, Adjust(color.R), Adjust(color.G), Adjust(color.B));
    }
}
