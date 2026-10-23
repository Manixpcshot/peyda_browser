using System.Globalization;

namespace PeydaBrowser.Core.Interfaces;

/// <summary>
/// UI string localization. Strings live in JSON resource files embedded in
/// PeydaBrowser.Core (one per language) so they can be unit-tested for key
/// parity and switched at runtime without restating layout. The brand name
/// "Peyda" stays identical in every language.
/// </summary>
public interface ILocalizationService
{
    string CurrentLanguage { get; }
    bool IsRightToLeft { get; }
    CultureInfo Culture { get; }
    event EventHandler? LanguageChanged;

    void SetLanguage(string language);
    string this[string key] { get; }
    string GetString(string key);
    string Format(string key, params object[] args);
    IReadOnlyCollection<string> GetKeys(string language);
}
