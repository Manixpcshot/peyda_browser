using PeydaBrowser.Core.Models;

namespace PeydaBrowser.WebEngine;

public enum InternalPage
{
    NewTab,
    Settings,
    History,
    Downloads,
    Bookmarks,
    About,
    PrivateHome,
    Error,
    Unknown
}

public sealed record InternalRoute(InternalPage Page, string? Argument);

/// <summary>
/// Routes peyda:// URLs to native pages. Internal pages are rendered with
/// native XAML instead of HTML inside WebView2: web content can therefore
/// never execute script in an internal page, and no custom URI scheme handler
/// is registered with the operating system or the web engine.
/// </summary>
public static class InternalPageRouter
{
    private static readonly Dictionary<string, InternalPage> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["newtab"] = InternalPage.NewTab,
        ["settings"] = InternalPage.Settings,
        ["history"] = InternalPage.History,
        ["downloads"] = InternalPage.Downloads,
        ["bookmarks"] = InternalPage.Bookmarks,
        ["about"] = InternalPage.About,
        ["private"] = InternalPage.PrivateHome
    };

    public static bool TryRoute(string? url, out InternalRoute route)
    {
        route = new InternalRoute(InternalPage.Unknown, null);
        if (string.IsNullOrWhiteSpace(url) || !PeydaUrl.IsPeydaUrl(url)) return false;

        var pageName = PeydaUrl.GetPageName(url);
        var page = Map.TryGetValue(pageName, out var mapped) ? mapped : InternalPage.Unknown;

        string? argument = null;
        var afterScheme = url[(PeydaUrl.Scheme.Length + 3)..];
        var slash = afterScheme.IndexOf('/');
        if (slash >= 0)
        {
            var rest = afterScheme[(slash + 1)..];
            var cut = rest.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) rest = rest[..cut];
            argument = Uri.UnescapeDataString(rest.Trim('/'));
            if (argument.Length > 256 || argument.Any(char.IsControl)) argument = null;
            if (string.IsNullOrEmpty(argument)) argument = null;
        }

        route = new InternalRoute(page, argument);
        return true;
    }

    public static string ToUrl(InternalPage page) => page switch
    {
        InternalPage.NewTab => PeydaUrl.NewTab,
        InternalPage.Settings => PeydaUrl.Settings,
        InternalPage.History => PeydaUrl.History,
        InternalPage.Downloads => PeydaUrl.Downloads,
        InternalPage.Bookmarks => PeydaUrl.Bookmarks,
        InternalPage.About => PeydaUrl.About,
        InternalPage.PrivateHome => PeydaUrl.Private,
        _ => PeydaUrl.NewTab
    };
}
