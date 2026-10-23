using System.Text.RegularExpressions;

namespace PeydaBrowser.Core.Updates;

public enum UpdateChannel
{
    Stable,
    Beta,
    Nightly
}

public static class UpdateChannelParser
{
    public static UpdateChannel Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "beta" => UpdateChannel.Beta,
        "nightly" => UpdateChannel.Nightly,
        _ => UpdateChannel.Stable
    };

    public static string ToSettingValue(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "beta",
        UpdateChannel.Nightly => "nightly",
        _ => "stable"
    };
}

/// <summary>Semantic Versioning 2.0.0 value with correct pre-release precedence.</summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease { get; }
    public bool IsPrerelease => Prerelease.Length > 0;

    private static readonly Regex VersionRegex = new(
        @"^v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z\-\.]+))?(?:\+[0-9A-Za-z\-\.]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private SemanticVersion(int major, int minor, int patch, string prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = VersionRegex.Match(text.Trim());
        if (!match.Success) return false;
        version = new SemanticVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : string.Empty);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a semantic version.");

    public int CompareTo(SemanticVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;   // release > prerelease
        if (!other.IsPrerelease) return -1;

        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        var length = Math.Max(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            if (i >= left.Length) return -1;  // fewer identifiers = lower precedence
            if (i >= right.Length) return 1;
            var l = left[i];
            var r = right[i];
            var lNum = long.TryParse(l, out var ln);
            var rNum = long.TryParse(r, out var rn);
            if (lNum && rNum)
            {
                if (ln != rn) return ln.CompareTo(rn);
            }
            else if (lNum) return -1;  // numeric < alphanumeric
            else if (rNum) return 1;
            else
            {
                c = string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
        }
        return 0;
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease.ToLowerInvariant());
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
    public static bool operator ==(SemanticVersion a, SemanticVersion b) => a.Equals(b);
    public static bool operator !=(SemanticVersion a, SemanticVersion b) => !a.Equals(b);
}

public sealed record GitHubAssetInfo(string Name, string DownloadUrl, long Size);

public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    string Body,
    string HtmlUrl,
    bool IsDraft,
    bool IsPrerelease,
    DateTimeOffset PublishedUtc,
    IReadOnlyList<GitHubAssetInfo> Assets);

/// <summary>Pure selection logic for "is there a newer release for my channel?".
/// Peyda never downloads or runs an update silently; the app only points the
/// user to the official GitHub release page (see docs/RELEASE.md).</summary>
public static class ReleaseSelector
{
    private static readonly Regex EmbeddedVersionRegex = new(
        @"(\d+\.\d+\.\d+(?:-[0-9A-Za-z\-\.]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryExtractVersion(GitHubReleaseInfo release, out SemanticVersion version)
    {
        if (SemanticVersion.TryParse(release.TagName, out version)) return true;
        var match = EmbeddedVersionRegex.Match(release.Name ?? string.Empty);
        if (match.Success && SemanticVersion.TryParse(match.Groups[1].Value, out version)) return true;
        version = default;
        return false;
    }

    public static UpdateChannel GetChannel(GitHubReleaseInfo release, SemanticVersion version)
    {
        if (!release.IsPrerelease && !version.IsPrerelease) return UpdateChannel.Stable;
        var pre = version.Prerelease.ToLowerInvariant();
        if (pre.Contains("beta") || pre.Contains("rc")) return UpdateChannel.Beta;
        return UpdateChannel.Nightly;
    }

    private static bool ChannelAllows(UpdateChannel userChannel, UpdateChannel releaseChannel) => userChannel switch
    {
        UpdateChannel.Stable => releaseChannel == UpdateChannel.Stable,
        UpdateChannel.Beta => releaseChannel is UpdateChannel.Stable or UpdateChannel.Beta,
        _ => true
    };

    public static GitHubReleaseInfo? SelectUpdate(
        IEnumerable<GitHubReleaseInfo> releases,
        SemanticVersion currentVersion,
        UpdateChannel channel)
    {
        GitHubReleaseInfo? best = null;
        var bestVersion = currentVersion;
        foreach (var release in releases)
        {
            if (release.IsDraft) continue;
            if (!TryExtractVersion(release, out var version)) continue;
            if (!ChannelAllows(channel, GetChannel(release, version))) continue;
            if (version > bestVersion)
            {
                best = release;
                bestVersion = version;
            }
        }
        return best;
    }
}
