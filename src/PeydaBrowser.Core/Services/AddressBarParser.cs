using System.Text.RegularExpressions;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;

namespace PeydaBrowser.Core.Services;

public enum AddressBarInputKind
{
    Url,
    Search,
    InternalPage
}

/// <summary>Result of parsing omnibox input. <see cref="Target"/> is a ready-to-navigate
/// URL for <see cref="AddressBarInputKind.Url"/>; for searches, <see cref="Query"/> and
/// <see cref="SearchEngineId"/> describe what still has to be turned into a URL.</summary>
public sealed record AddressBarParseResult(
    AddressBarInputKind Kind,
    string Target,
    string? Query,
    string? SearchEngineId,
    string? InternalPage);

/// <summary>
/// Decides whether omnibox text is a URL, a domain, an internal peyda:// page,
/// a bang search ("!g cats") or a plain search query. The rules deliberately
/// mirror mainstream browsers: explicit http/https URLs navigate, domain-like
/// input gets https:// prepended, everything else is searched.
/// </summary>
public sealed class AddressBarParser
{
    private static readonly Regex LabelRegex = new(
        @"^[\p{L}\p{N}]([\p{L}\p{N}-]*[\p{L}\p{N}])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TldRegex = new(
        @"^(\p{L}{2,63}|xn--[a-z0-9-]{2,59})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Ipv4Regex = new(
        @"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="bangToEngineId">Map of bang shortcut (without '!') to search engine id.</param>
    public AddressBarParseResult Parse(string input, IReadOnlyDictionary<string, string>? bangToEngineId = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        if (text.Length == 0)
            return new AddressBarParseResult(AddressBarInputKind.Search, string.Empty, string.Empty, null, null);

        // Internal pages: peyda://settings, peyda://newtab, ...
        if (PeydaUrl.IsPeydaUrl(text))
        {
            var page = PeydaUrl.GetPageName(text);
            return new AddressBarParseResult(AddressBarInputKind.InternalPage,
                PeydaUrl.Scheme + "://" + page, null, null, page);
        }

        if (string.Equals(text, "about:blank", StringComparison.OrdinalIgnoreCase))
            return Url("about:blank");

        // Bang searches: "!g query", "!d query" ...
        if (text.StartsWith('!') && bangToEngineId is { Count: > 0 })
        {
            var space = text.IndexOf(' ');
            var bang = (space < 0 ? text[1..] : text[1..space]).ToLowerInvariant();
            if (bang.Length > 0 && bangToEngineId.TryGetValue(bang, out var engineId))
            {
                var query = space < 0 ? string.Empty : text[(space + 1)..].Trim();
                return new AddressBarParseResult(AddressBarInputKind.Search, query, query, engineId, null);
            }
        }

        // Explicit scheme URLs: only http/https ever navigate from the omnibox.
        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (UrlValidator.IsValidHttpUrl(text, out var uri) && uri is not null)
                return Url(NormalizeHttpUrl(uri));
            return Search(text);
        }

        // Domain-like input without a scheme: host[:port][/path?query#fragment]
        if (!text.Any(char.IsWhiteSpace) && TryMakeDomainUrl(text, out var domainUrl))
            return Url(domainUrl);

        return Search(text);
    }

    private static AddressBarParseResult Url(string url) =>
        new(AddressBarInputKind.Url, url, null, null, null);

    private static AddressBarParseResult Search(string query) =>
        new(AddressBarInputKind.Search, query, query, null, null);

    private static string NormalizeHttpUrl(Uri uri)
    {
        // Uri.AbsoluteUri already punycode-encodes IDN hosts, which is what the engine needs.
        return uri.AbsoluteUri;
    }

    internal static bool TryMakeDomainUrl(string text, out string url)
    {
        url = string.Empty;
        var cut = text.IndexOfAny(new[] { '/', '?', '#' });
        var hostPort = cut < 0 ? text : text[..cut];
        var rest = cut < 0 ? string.Empty : text[cut..];
        if (hostPort.Length == 0) return false;

        string host;
        string? port = null;

        if (hostPort.StartsWith('['))
        {
            // IPv6 literal: [::1] or [::1]:8080
            var close = hostPort.IndexOf(']');
            if (close < 0) return false;
            host = hostPort[..(close + 1)];
            var tail = hostPort[(close + 1)..];
            if (tail.Length > 0)
            {
                if (!tail.StartsWith(':') || !IsValidPort(tail[1..])) return false;
                port = tail[1..];
            }
            var inner = host[1..^1];
            if (!System.Net.IPAddress.TryParse(inner, out var ip) ||
                ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                return false;
        }
        else
        {
            var colon = hostPort.LastIndexOf(':');
            if (colon >= 0)
            {
                var possiblePort = hostPort[(colon + 1)..];
                if (!IsValidPort(possiblePort)) return false;
                port = possiblePort;
                host = hostPort[..colon];
            }
            else
            {
                host = hostPort;
            }
            if (!IsDomainLikeHost(host)) return false;
        }

        var asciiHost = host.StartsWith('[') ? host : UrlValidator.ToAsciiHost(host);
        if (asciiHost is null) return false;

        // Loopback development servers almost always speak plain HTTP; every
        // other host is upgraded to HTTPS by default.
        var isLocal = host.StartsWith('[') ||
                      string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                      System.Net.IPAddress.TryParse(host, out _);
        var scheme = isLocal ? "http" : "https";
        url = scheme + "://" + asciiHost.ToLowerInvariant() + (port is null ? "" : ":" + port) + rest;
        return true;
    }

    internal static bool IsDomainLikeHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        var ipv4 = Ipv4Regex.Match(host);
        if (ipv4.Success)
            return ipv4.Groups.Cast<Group>().Skip(1).All(g => int.TryParse(g.Value, out var n) && n <= 255);

        if (!host.Contains('.')) return false;
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2 || labels.Length != host.Split('.').Length) return false;
        if (!labels.All(l => l.Length <= 63 && LabelRegex.IsMatch(l))) return false;
        return TldRegex.IsMatch(labels[^1]);
    }

    private static bool IsValidPort(string value) =>
        int.TryParse(value, out var port) && port is >= 1 and <= 65535;
}
