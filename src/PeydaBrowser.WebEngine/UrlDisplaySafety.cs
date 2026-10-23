using System.Globalization;

namespace PeydaBrowser.WebEngine;

public sealed record UrlDisplayInfo(
    string DisplayHost,
    bool IsSecure,
    bool IsInternal,
    bool IsIdnHomographRisk,
    string Scheme);

/// <summary>
/// Decides how a URL's host should be shown in the address bar. Internationalized
/// domain names are displayed in Unicode only when they use a single writing
/// system; mixed-script hosts (a classic homograph/phishing signal, e.g. Latin +
/// Cyrillic) are shown in punycode so the user is not deceived.
/// </summary>
public static class UrlDisplaySafety
{
    public static UrlDisplayInfo Describe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new UrlDisplayInfo(string.Empty, false, false, false, string.Empty);

        var isInternal = uri.Scheme.Equals("peyda", StringComparison.OrdinalIgnoreCase);
        var isSecure = uri.Scheme == Uri.UriSchemeHttps || isInternal ||
                       string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        var asciiHost = uri.Host;
        var unicodeHost = ToUnicodeHost(asciiHost) ?? asciiHost;
        var risk = HasMixedScripts(unicodeHost);
        var display = risk ? asciiHost : unicodeHost;

        return new UrlDisplayInfo(display, isSecure, isInternal, risk, uri.Scheme);
    }

    public static string? ToUnicodeHost(string asciiHost)
    {
        try
        {
            return new IdnMapping().GetUnicode(asciiHost);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool HasMixedScripts(string host)
    {
        var scripts = new HashSet<string>();
        foreach (var c in host)
        {
            var script = GetScript(c);
            if (script is not null) scripts.Add(script);
            if (scripts.Count > 1) return true;
        }
        return false;
    }

    /// <summary>Very small script classifier covering common spoofing pairs. Digits,
    /// hyphens and dots are script-neutral and return null.</summary>
    private static string? GetScript(char c)
    {
        if (c <= 0x024F && char.IsLetter(c)) return "Latin";
        return c switch
        {
            >= '\u0370' and <= '\u03FF' => "Greek",
            >= '\u0400' and <= '\u04FF' => "Cyrillic",
            >= '\u0530' and <= '\u058F' => "Armenian",
            >= '\u0590' and <= '\u05FF' => "Hebrew",
            >= '\u0600' and <= '\u06FF' => "Arabic",
            >= '\u0750' and <= '\u077F' => "Arabic",
            >= '\u08A0' and <= '\u08FF' => "Arabic",
            >= '\u0900' and <= '\u097F' => "Devanagari",
            >= '\u10A0' and <= '\u10FF' => "Georgian",
            >= '\u3040' and <= '\u30FF' => "JapaneseKana",
            >= '\u4E00' and <= '\u9FFF' => "Han",
            >= '\uAC00' and <= '\uD7A3' => "Hangul",
            _ => char.IsLetter(c) ? "Other" : null
        };
    }
}
