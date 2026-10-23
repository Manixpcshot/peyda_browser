namespace PeydaBrowser.Theming;

public sealed record ThemeCatalogEntry(string Id, bool IsBuiltIn, ThemeDefinition Definition);

public static class BuiltInThemes
{
    public const string LightId = "peyda-light";
    public const string DarkId = "peyda-dark";
    public const string MidnightId = "peyda-midnight";
    public const string SunsetId = "peyda-sunset";

    public static ThemeCatalogEntry PeydaLight { get; } = new(LightId, true, new ThemeDefinition
    {
        Name = "Peyda Light",
        Mode = "light",
        Accent = "#6D28D9",
        Background = "#F6F7FB",
        Surface = "#FFFFFF",
        Toolbar = "#EEF0F7",
        TextPrimary = "#111827",
        TextSecondary = "#5B6474",
        Border = "#D8DCE8",
        TabActive = "#FFFFFF",
        TabInactive = "#E6E9F2",
        Transparency = 0.97
    });

    public static ThemeCatalogEntry PeydaDark { get; } = new(DarkId, true, new ThemeDefinition
    {
        Name = "Peyda Dark",
        Mode = "dark",
        Accent = "#7C3AED",
        Background = "#14161D",
        Surface = "#1C1F29",
        Toolbar = "#191C26",
        TextPrimary = "#F3F4F8",
        TextSecondary = "#9AA3B5",
        Border = "#2C3040",
        TabActive = "#232635",
        TabInactive = "#171A23",
        Transparency = 0.96
    });

    /// <summary>The sample theme from the product specification.</summary>
    public static ThemeCatalogEntry PeydaMidnight { get; } = new(MidnightId, true, new ThemeDefinition
    {
        Name = "Peyda Midnight",
        Mode = "dark",
        Accent = "#7C3AED",
        Background = "#0F1117",
        Surface = "#171A23",
        Toolbar = "#151821",
        TextPrimary = "#F8FAFC",
        TextSecondary = "#94A3B8",
        Border = "#262B3A",
        TabActive = "#1D2130",
        TabInactive = "#11141D",
        TabStyle = "rounded",
        TabPosition = "top",
        Density = "normal",
        SidebarEnabled = true,
        Transparency = 0.95
    });

    public static ThemeCatalogEntry PeydaSunset { get; } = new(SunsetId, true, new ThemeDefinition
    {
        Name = "Peyda Sunset",
        Mode = "dark",
        Accent = "#F59E0B",
        Background = "#1B1310",
        Surface = "#261A14",
        Toolbar = "#201611",
        TextPrimary = "#FFF7ED",
        TextSecondary = "#C7A68B",
        Border = "#3D2A1E",
        TabActive = "#2E2016",
        TabInactive = "#1D140F",
        Transparency = 0.96
    });

    public static IReadOnlyList<ThemeCatalogEntry> All { get; } =
        new[] { PeydaLight, PeydaDark, PeydaMidnight, PeydaSunset };

    public static ThemeCatalogEntry GetByIdOrDefault(string? id, IEnumerable<ThemeCatalogEntry>? customThemes = null)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var all = customThemes is null ? All : All.Concat(customThemes);
            var found = all.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
        }
        return PeydaDark;
    }
}
