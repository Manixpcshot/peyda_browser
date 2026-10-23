using System.Text.Json;

namespace PeydaBrowser.Theming;

/// <summary>JSON import/export for themes. Parsing is fully defensive: any
/// malformed input yields the default theme plus error keys, never an exception.</summary>
public static class ThemeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ToJson(ThemeDefinition theme)
    {
        var clean = ThemeValidator.Validate(theme).Theme;
        return JsonSerializer.Serialize(clean, Options);
    }

    public static ThemeValidationResult FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(), new[] { "theme.json.empty" });

        if (json.Length > 64 * 1024)
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(), new[] { "theme.json.tooLarge" });

        try
        {
            var parsed = JsonSerializer.Deserialize<ThemeDefinition>(json, Options);
            return ThemeValidator.Validate(parsed);
        }
        catch (JsonException)
        {
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(), new[] { "theme.json.invalid" });
        }
    }
}
