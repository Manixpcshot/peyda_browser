using System.Text.RegularExpressions;

namespace PeydaBrowser.Theming;

public sealed record ThemeValidationResult(ThemeDefinition Theme, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Sanitizes an untrusted theme. Invalid values are replaced with safe
/// defaults for the theme mode and reported as error keys; this validator
/// never throws, so a corrupt theme file cannot crash the browser.
/// </summary>
public static class ThemeValidator
{
    private static readonly Regex HexColor = new("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex FontName = new(@"^[\p{L}\p{N} ,\-]{1,100}$", RegexOptions.Compiled);

    private static readonly string[] Modes = { "light", "dark" };
    private static readonly string[] TabStyles = { "rounded", "square" };
    private static readonly string[] TabPositions = { "top", "left" };
    private static readonly string[] Densities = { "compact", "normal", "comfortable" };

    public static ThemeValidationResult Validate(ThemeDefinition? theme)
    {
        var errors = new List<string>();
        if (theme is null)
        {
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(),
                new[] { "theme.null" });
        }

        var mode = NormalizeEnum(theme.Mode, Modes, "dark", errors, "theme.mode");
        var fallback = (mode == "light" ? BuiltInThemes.PeydaLight : BuiltInThemes.PeydaDark).Definition;

        var clean = new ThemeDefinition
        {
            SchemaVersion = theme.SchemaVersion <= 0 ? 1 : Math.Min(theme.SchemaVersion, 1),
            Name = SanitizeName(theme.Name, fallback.Name, errors),
            Mode = mode,
            Accent = Color(theme.Accent, fallback.Accent, errors, "theme.accent"),
            Background = Color(theme.Background, fallback.Background, errors, "theme.background"),
            Surface = Color(theme.Surface, fallback.Surface, errors, "theme.surface"),
            Toolbar = Color(theme.Toolbar, fallback.Toolbar, errors, "theme.toolbar"),
            TextPrimary = Color(theme.TextPrimary, fallback.TextPrimary, errors, "theme.textPrimary"),
            TextSecondary = Color(theme.TextSecondary, fallback.TextSecondary, errors, "theme.textSecondary"),
            Border = Color(theme.Border, fallback.Border, errors, "theme.border"),
            TabActive = Color(theme.TabActive, fallback.TabActive, errors, "theme.tabActive"),
            TabInactive = Color(theme.TabInactive, fallback.TabInactive, errors, "theme.tabInactive"),
            TabStyle = NormalizeEnum(theme.TabStyle, TabStyles, "rounded", errors, "theme.tabStyle"),
            TabPosition = NormalizeEnum(theme.TabPosition, TabPositions, "top", errors, "theme.tabPosition"),
            Density = NormalizeEnum(theme.Density, Densities, "normal", errors, "theme.density"),
            SidebarEnabled = theme.SidebarEnabled,
            Transparency = Clamp(theme.Transparency, 0.5, 1.0, errors, "theme.transparency"),
            CornerRadius = Clamp(theme.CornerRadius, 0, 24, errors, "theme.cornerRadius"),
            ToolbarHeight = Clamp(theme.ToolbarHeight, 36, 72, errors, "theme.toolbarHeight"),
            UiFontFamily = SanitizeFont(theme.UiFontFamily, fallback.UiFontFamily, errors)
        };

        if (theme.SchemaVersion > 1) errors.Add("theme.schemaVersion.newer");
        return new ThemeValidationResult(clean, errors);
    }

    private static string Color(string? value, string fallback, List<string> errors, string key)
    {
        if (value is not null && HexColor.IsMatch(value.Trim()))
            return value.Trim().ToUpperInvariant();
        errors.Add(key);
        return fallback;
    }

    private static string NormalizeEnum(string? value, string[] allowed, string fallback, List<string> errors, string key)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (allowed.Contains(normalized)) return normalized;
        errors.Add(key);
        return fallback;
    }

    private static double Clamp(double value, double min, double max, List<string> errors, string key)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            errors.Add(key);
            return double.IsNaN(value) ? min : Math.Clamp(value, min, max);
        }
        return value;
    }

    private static string SanitizeName(string? name, string fallback, List<string> errors)
    {
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0 || clean.Length > 60)
        {
            errors.Add("theme.name");
            return clean.Length > 60 ? clean[..60] : fallback;
        }
        return clean;
    }

    private static string SanitizeFont(string? font, string fallback, List<string> errors)
    {
        var value = (font ?? string.Empty).Trim();
        if (value.Length > 0 && FontName.IsMatch(value)) return value;
        errors.Add("theme.uiFontFamily");
        return fallback;
    }
}
