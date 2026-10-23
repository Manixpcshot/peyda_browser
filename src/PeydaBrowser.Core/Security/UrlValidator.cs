using System.Globalization;

namespace PeydaBrowser.Core.Security;

/// <summary>
/// Validation helpers for URLs typed, pasted or imported by the user.
/// Everything here is defensive: untrusted input must never reach the web
/// engine (or the OS shell) unexamined.
/// </summary>
public static class UrlValidator
{
    public const int MaxUrlLength = 8192;

    /// <summary>Schemes Peyda navigates to without asking the operating system.</summary>
    public static readonly IReadOnlySet<string> NavigableSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "about", "peyda", "data", "blob"
    };

    /// <summary>Schemes that must never be navigated to or launched silently.</summary>
    public static readonly IReadOnlySet<string> BlockedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "vbscript", "file", "ms-msdt", "search-ms"
    };

    public static bool IsValidHttpUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUrlLength) return false;
        if (value.Any(char.IsControl)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(parsed.Host)) return false;
        uri = parsed;
        return true;
    }

    /// <summary>
    /// Converts a Unicode host name to its ASCII (punycode) form. Returns null
    /// when the host cannot be converted, in which case callers should treat
    /// the input as a search query instead of a navigation.
    /// </summary>
    public static string? ToAsciiHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        try
        {
            return new IdnMapping().GetAscii(host.Trim().TrimEnd('.'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Canonical origin ("scheme://host[:port]") used as the permission key. Null for opaque origins.</summary>
    public static string? GetOrigin(string? url)
    {
        if (!IsValidHttpUrl(url, out var uri) || uri is null) return null;
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
