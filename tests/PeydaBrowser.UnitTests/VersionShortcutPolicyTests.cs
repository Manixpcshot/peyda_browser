using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Shortcuts;
using PeydaBrowser.Core.Updates;
using PeydaBrowser.WebEngine;

namespace PeydaBrowser.UnitTests;

public class SemanticVersionTests
{
    [Fact]
    public void Precedence_FollowsSemVerSpec()
    {
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0"
        }.Select(SemanticVersion.Parse).ToArray();

        for (var i = 0; i < ordered.Length - 1; i++)
            Assert.True(ordered[i] < ordered[i + 1], $"{ordered[i]} should be < {ordered[i + 1]}");
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("0.1.0-alpha", "0.1.0-alpha")]
    [InlineData("1.0.0+build.42", "1.0.0")]
    public void Parse_HandlesTagsAndBuildMetadata(string input, string expected)
    {
        Assert.Equal(expected, SemanticVersion.Parse(input).ToString());
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("banana")]
    [InlineData("")]
    public void Parse_RejectsInvalid(string input)
    {
        Assert.False(SemanticVersion.TryParse(input, out _));
    }

    private static GitHubReleaseInfo Release(string tag, bool prerelease = false, bool draft = false, string name = "") => new(
        tag, name.Length == 0 ? tag : name, "notes", "https://github.com/x/y/releases/" + tag,
        draft, prerelease, DateTimeOffset.UtcNow, Array.Empty<GitHubAssetInfo>());

    [Fact]
    public void StableChannel_IgnoresPrereleases()
    {
        var releases = new[] { Release("v1.1.0-beta.1", prerelease: true), Release("v1.0.5") };
        var update = ReleaseSelector.SelectUpdate(releases, SemanticVersion.Parse("1.0.0"), UpdateChannel.Stable);
        Assert.NotNull(update);
        Assert.Equal("v1.0.5", update!.TagName);
    }

    [Fact]
    public void BetaChannel_SeesBetas_DraftsAreIgnored()
    {
        var releases = new[] { Release("v2.0.0", draft: true), Release("v1.1.0-beta.1", prerelease: true) };
        var update = ReleaseSelector.SelectUpdate(releases, SemanticVersion.Parse("1.0.0"), UpdateChannel.Beta);
        Assert.Equal("v1.1.0-beta.1", update!.TagName);
    }

    [Fact]
    public void NightlyChannel_ExtractsVersionFromReleaseName()
    {
        var releases = new[] { Release("nightly", prerelease: true, name: "Peyda 0.2.0-nightly.20261023") };
        var update = ReleaseSelector.SelectUpdate(releases, SemanticVersion.Parse("0.1.0-alpha"), UpdateChannel.Nightly);
        Assert.NotNull(update);
    }

    [Fact]
    public void NoNewerVersion_ReturnsNull()
    {
        var releases = new[] { Release("v1.0.0") };
        Assert.Null(ReleaseSelector.SelectUpdate(releases, SemanticVersion.Parse("1.0.0"), UpdateChannel.Stable));
    }
}

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Shift+T", "Ctrl+Shift+T")]
    [InlineData("ctrl+l", "Ctrl+L")]
    [InlineData("Alt+Left", "Alt+Left")]
    [InlineData("F11", "F11")]
    [InlineData("Ctrl+Tab", "Ctrl+Tab")]
    public void Gesture_RoundTripsCanonically(string input, string expected)
    {
        Assert.Equal(expected, ShortcutGesture.Parse(input).ToString());
    }

    [Theory]
    [InlineData("Ctrl+Ctrl+T")]
    [InlineData("Ctrl+")]
    [InlineData("Banana+T")]
    [InlineData("")]
    public void Gesture_RejectsInvalid(string input)
    {
        Assert.False(ShortcutGesture.TryParse(input, out _));
    }

    [Fact]
    public void DefaultShortcuts_HaveNoConflicts_AndCoverSpec()
    {
        Assert.False(DefaultShortcuts.HasConflicts(DefaultShortcuts.All));
        var gestures = DefaultShortcuts.All.Select(d => d.Gesture.ToString()).ToHashSet();
        foreach (var required in new[]
        {
            "Ctrl+T", "Ctrl+W", "Ctrl+Shift+T", "Ctrl+L", "Alt+D", "Ctrl+R", "Ctrl+Shift+R",
            "Alt+Left", "Alt+Right", "Ctrl+D", "Ctrl+H", "Ctrl+J", "Ctrl+Shift+N",
            "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+1", "Ctrl+8", "Ctrl+9", "F5", "F11", "F12"
        })
        {
            Assert.Contains(required, gestures);
        }
    }
}

public class NavigationPolicyTests
{
    [Theory]
    [InlineData("https://example.com", NavigationAction.Allow)]
    [InlineData("http://example.com", NavigationAction.Allow)]
    [InlineData("about:blank", NavigationAction.Allow)]
    [InlineData("javascript:alert(1)", NavigationAction.Block)]
    [InlineData("data:text/html,<b>x</b>", NavigationAction.Allow)]
    [InlineData("mailto:a@example.com", NavigationAction.AskExternalApp)]
    [InlineData("tel:+123", NavigationAction.AskExternalApp)]
    [InlineData("peyda://settings", NavigationAction.OpenInternalPage)]
    [InlineData("", NavigationAction.Block)]
    public void Decide_ClassifiesMainFrameNavigations(string url, NavigationAction expected)
    {
        Assert.Equal(expected, NavigationPolicy.Decide(url).Action);
    }

    [Fact]
    public void FileScheme_FromWebContent_IsBlocked_ButUserTypedIsAllowed()
    {
        Assert.Equal(NavigationAction.Block, NavigationPolicy.Decide("file:///C:/secret.txt", isUserTyped: false).Action);
        Assert.Equal(NavigationAction.Allow, NavigationPolicy.Decide("file:///C:/page.html", isUserTyped: true).Action);
    }

    [Fact]
    public void InternalPage_InSubframe_IsBlocked()
    {
        Assert.Equal(NavigationAction.Block, NavigationPolicy.Decide("peyda://settings", isMainFrame: false).Action);
    }

    [Fact]
    public void Router_ParsesPagesAndArguments()
    {
        Assert.True(InternalPageRouter.TryRoute("peyda://settings/search", out var route));
        Assert.Equal(InternalPage.Settings, route.Page);
        Assert.Equal("search", route.Argument);

        Assert.True(InternalPageRouter.TryRoute("peyda://nope", out var unknown));
        Assert.Equal(InternalPage.Unknown, unknown.Page);

        Assert.False(InternalPageRouter.TryRoute("https://example.com", out _));
    }

    [Fact]
    public void PopupPolicy_BlocksUnsolicitedByDefault()
    {
        Assert.False(PermissionPolicy.ShouldAllowPopup(isUserInitiated: false, blockPopupsSetting: true));
        Assert.True(PermissionPolicy.ShouldAllowPopup(isUserInitiated: true, blockPopupsSetting: true));
        Assert.True(PermissionPolicy.ShouldAllowPopup(isUserInitiated: false, blockPopupsSetting: false));
    }

    [Theory]
    [InlineData("Camera", BrowserPermissionKind.Camera)]
    [InlineData("Microphone", BrowserPermissionKind.Microphone)]
    [InlineData("Geolocation", BrowserPermissionKind.Geolocation)]
    [InlineData("Notifications", BrowserPermissionKind.Notifications)]
    [InlineData("ClipboardRead", BrowserPermissionKind.ClipboardRead)]
    [InlineData("SomethingNew", BrowserPermissionKind.Other)]
    public void PermissionKinds_MapFromWebView2Names(string input, BrowserPermissionKind expected)
    {
        Assert.Equal(expected, PermissionPolicy.MapKind(input));
    }
}

public class UrlDisplaySafetyTests
{
    [Fact]
    public void PureLatinDomain_ShowsAsIs_AndHttpsIsSecure()
    {
        var info = UrlDisplaySafety.Describe("https://example.com/path");
        Assert.Equal("example.com", info.DisplayHost);
        Assert.True(info.IsSecure);
        Assert.False(info.IsIdnHomographRisk);
    }

    [Fact]
    public void MixedScriptIdn_ShowsPunycode()
    {
        // Latin "e" + Cyrillic look-alike in one label is a homograph red flag.
        const string mixed = "ex\u0430mple.com"; // contains Cyrillic а
        Assert.True(UrlDisplaySafety.HasMixedScripts(mixed));
    }

    [Fact]
    public void SingleScriptIdn_IsNotFlagged()
    {
        Assert.False(UrlDisplaySafety.HasMixedScripts("مثال.ایران"));
        Assert.False(UrlDisplaySafety.HasMixedScripts("пример.рф"));
    }

    [Fact]
    public void Http_IsNotSecure_ButLocalhostIsTrusted()
    {
        Assert.False(UrlDisplaySafety.Describe("http://example.com").IsSecure);
        Assert.True(UrlDisplaySafety.Describe("http://localhost:8080").IsSecure);
        Assert.True(UrlDisplaySafety.Describe("peyda://settings").IsInternal);
    }
}
