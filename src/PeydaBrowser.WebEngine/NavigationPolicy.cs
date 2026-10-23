using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;

namespace PeydaBrowser.WebEngine;

public enum NavigationAction
{
    Allow,
    Block,
    OpenInternalPage,
    AskExternalApp
}

public sealed record NavigationDecision(NavigationAction Action, InternalRoute? InternalRoute, string Reason)
{
    public static NavigationDecision Allow() => new(NavigationAction.Allow, null, "allowed scheme");
    public static NavigationDecision Block(string reason) => new(NavigationAction.Block, null, reason);
}

/// <summary>
/// Central gatekeeper consulted for every main/sub-frame navigation and every
/// "new window" request before WebView2 is allowed to proceed.
/// </summary>
public static class NavigationPolicy
{
    public static NavigationDecision Decide(string? targetUrl, bool isMainFrame = true, bool isUserTyped = false)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            return NavigationDecision.Block("empty URL");
        if (targetUrl.Length > UrlValidator.MaxUrlLength)
            return NavigationDecision.Block("URL too long");

        if (PeydaUrl.IsPeydaUrl(targetUrl))
        {
            // Internal pages may only be opened as top-level user/app navigations;
            // web content must not be able to embed or redirect into them silently.
            if (!isMainFrame) return NavigationDecision.Block("internal page in subframe");
            InternalPageRouter.TryRoute(targetUrl, out var route);
            return new NavigationDecision(NavigationAction.OpenInternalPage, route, "internal page");
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
            return NavigationDecision.Block("malformed URL");

        if (UrlValidator.BlockedSchemes.Contains(uri.Scheme))
        {
            // Local files typed explicitly by the user are the single exception:
            // a web page must never navigate the frame to file:// or script schemes.
            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) && isUserTyped && isMainFrame)
                return NavigationDecision.Allow();
            return NavigationDecision.Block($"scheme '{uri.Scheme}' is blocked");
        }

        if (UrlValidator.NavigableSchemes.Contains(uri.Scheme))
            return NavigationDecision.Allow();

        // Any other scheme (mailto:, tel:, custom app protocols, ...) is handed to
        // the OS only after explicit user confirmation, and only for the main frame.
        return isMainFrame
            ? new NavigationDecision(NavigationAction.AskExternalApp, null, $"external scheme '{uri.Scheme}'")
            : NavigationDecision.Block($"external scheme '{uri.Scheme}' in subframe");
    }
}

public static class PermissionPolicy
{
    /// <summary>Maps WebView2 permission-kind names (passed as strings to keep this
    /// project WebView2-free) to Peyda permission kinds.</summary>
    public static BrowserPermissionKind MapKind(string? webView2PermissionKindName) => webView2PermissionKindName switch
    {
        "Camera" => BrowserPermissionKind.Camera,
        "Microphone" => BrowserPermissionKind.Microphone,
        "Geolocation" => BrowserPermissionKind.Geolocation,
        "Notifications" => BrowserPermissionKind.Notifications,
        "ClipboardRead" => BrowserPermissionKind.ClipboardRead,
        _ => BrowserPermissionKind.Other
    };

    /// <summary>Default when a site has no stored decision: ask the user. Sensitive
    /// capabilities are never granted silently.</summary>
    public static PermissionDecision DefaultFor(BrowserPermissionKind kind) => kind switch
    {
        BrowserPermissionKind.Camera => PermissionDecision.Ask,
        BrowserPermissionKind.Microphone => PermissionDecision.Ask,
        BrowserPermissionKind.Geolocation => PermissionDecision.Ask,
        BrowserPermissionKind.Notifications => PermissionDecision.Ask,
        BrowserPermissionKind.ClipboardRead => PermissionDecision.Ask,
        _ => PermissionDecision.Ask
    };

    /// <summary>Pop-up rule: user-clicked window opens are allowed (as a new tab);
    /// unsolicited ones follow the "block pop-ups" setting (on by default).</summary>
    public static bool ShouldAllowPopup(bool isUserInitiated, bool blockPopupsSetting) =>
        isUserInitiated || !blockPopupsSetting;
}
