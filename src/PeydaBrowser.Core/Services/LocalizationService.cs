using System.Globalization;
using System.Text.Json;
using PeydaBrowser.Core.Interfaces;

namespace PeydaBrowser.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { "en", "fa" };
    public const string FallbackLanguage = "en";

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage { get; private set; } = FallbackLanguage;
    public bool IsRightToLeft => CurrentLanguage == "fa";

    public CultureInfo Culture => CurrentLanguage == "fa"
        ? new CultureInfo("fa-IR")
        : new CultureInfo("en-US");

    public event EventHandler? LanguageChanged;

    /// <param name="catalogsOverride">Test hook: language -> (key -> text). When null,
    /// the embedded JSON resources are used.</param>
    public LocalizationService(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? catalogsOverride = null)
    {
        if (catalogsOverride is not null)
        {
            foreach (var (language, catalog) in catalogsOverride)
                _catalogs[language] = catalog;
        }
        else
        {
            foreach (var language in SupportedLanguages)
                _catalogs[language] = LoadEmbedded(language);
        }
    }

    public void SetLanguage(string language)
    {
        var normalized = (language ?? FallbackLanguage).Trim().ToLowerInvariant();
        if (!_catalogs.ContainsKey(normalized)) normalized = FallbackLanguage;
        if (normalized == CurrentLanguage) return;
        CurrentLanguage = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (_catalogs.TryGetValue(CurrentLanguage, out var catalog) && catalog.TryGetValue(key, out var value))
            return value;
        if (_catalogs.TryGetValue(FallbackLanguage, out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;
        return key;
    }

    public string Format(string key, params object[] args)
    {
        var pattern = GetString(key);
        try
        {
            return string.Format(Culture, pattern, args);
        }
        catch (FormatException)
        {
            return pattern;
        }
    }

    public IReadOnlyCollection<string> GetKeys(string language) =>
        _catalogs.TryGetValue(language, out var catalog) ? catalog.Keys.ToArray() : Array.Empty<string>();

    private static IReadOnlyDictionary<string, string> LoadEmbedded(string language)
    {
        var assembly = typeof(LocalizationService).Assembly;
        // Resolve by suffix: the exact manifest name depends on MSBuild's
        // LogicalName rules, which vary with project layout.
        var suffix = $".Strings.{language}.json";
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return new Dictionary<string, string>();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return new Dictionary<string, string>();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }
}
