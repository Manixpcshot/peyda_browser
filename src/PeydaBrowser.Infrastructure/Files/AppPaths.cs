using System.Text.RegularExpressions;

namespace PeydaBrowser.Infrastructure.Files;

/// <summary>
/// Single source of truth for on-disk locations. Everything user-specific lives
/// under %LOCALAPPDATA%\PeydaBrowser with one isolated folder per profile:
/// &lt;root&gt;/Profiles/&lt;id&gt;/peyda-profile.db, .../WebView2/, .../Logs/, .../Cache/.
/// </summary>
public sealed class AppPaths
{
    private static readonly Regex SafeId = new("^[a-zA-Z0-9-]{1,64}$", RegexOptions.Compiled);

    public string RootDirectory { get; }

    public AppPaths(string? rootOverride = null)
    {
        RootDirectory = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeydaBrowser");
    }

    public string ProfilesDirectory => Path.Combine(RootDirectory, "Profiles");
    public string ProfilesIndexPath => Path.Combine(RootDirectory, "profiles.json");

    public string ProfileDirectory(string profileId) =>
        Path.Combine(ProfilesDirectory, SanitizeProfileId(profileId));

    public string ProfileDatabasePath(string profileId) =>
        Path.Combine(ProfileDirectory(profileId), "peyda-profile.db");

    public string ProfileWebView2Directory(string profileId) =>
        Path.Combine(ProfileDirectory(profileId), "WebView2");

    public string ProfileLogsDirectory(string profileId) =>
        Path.Combine(ProfileDirectory(profileId), "Logs");

    public string ProfileCacheDirectory(string profileId) =>
        Path.Combine(ProfileDirectory(profileId), "Cache");

    /// <summary>Creates a throw-away directory for private/guest profiles under the OS temp folder.</summary>
    public string CreateEphemeralDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PeydaBrowser", "Private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void EnsureProfileDirectories(string profileId)
    {
        Directory.CreateDirectory(ProfileDirectory(profileId));
        Directory.CreateDirectory(ProfileWebView2Directory(profileId));
        Directory.CreateDirectory(ProfileLogsDirectory(profileId));
        Directory.CreateDirectory(ProfileCacheDirectory(profileId));
    }

    public static string SanitizeProfileId(string profileId)
    {
        if (profileId is not null && SafeId.IsMatch(profileId)) return profileId;
        // Never let an untrusted id escape the profiles directory (path traversal).
        return "profile-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(profileId ?? string.Empty)))[..16].ToLowerInvariant();
    }

    public static void DeleteDirectorySafe(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: WebView2 may still hold file locks briefly after window close.
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (DirectoryNotFoundException) { }
        }
        catch (UnauthorizedAccessException) { }
    }
}
