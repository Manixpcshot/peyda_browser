using PeydaBrowser.Theming;

namespace PeydaBrowser.Infrastructure.Files;

/// <summary>Theme import/export to .json files. Imported files are size-capped and
/// fully validated; invalid files can never crash or corrupt the app.</summary>
public sealed class ThemeFileService
{
    public const long MaxThemeFileBytes = 64 * 1024;

    public async Task<ThemeValidationResult> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(), new[] { "theme.file.missing" });
        if (info.Length > MaxThemeFileBytes)
            return new ThemeValidationResult(BuiltInThemes.PeydaDark.Definition.Clone(), new[] { "theme.file.tooLarge" });

        var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return ThemeSerializer.FromJson(json);
    }

    public Task ExportAsync(ThemeDefinition theme, string filePath, CancellationToken ct = default) =>
        File.WriteAllTextAsync(filePath, ThemeSerializer.ToJson(theme), ct);
}
