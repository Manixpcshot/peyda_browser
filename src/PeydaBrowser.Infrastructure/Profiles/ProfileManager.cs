using System.Text.Json;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Infrastructure.Files;

namespace PeydaBrowser.Infrastructure.Profiles;

/// <summary>Everything a window needs to run for one profile.</summary>
public sealed record ProfileContext(
    BrowserProfile Profile,
    string ProfileDirectory,
    string DatabasePath,
    string WebView2UserDataDirectory,
    string LogsDirectory,
    bool IsPrivate);

public sealed record ProfileDescriptor(string Id, string Name, string ColorHex, DateTimeOffset CreatedUtc, bool IsGuest);

/// <summary>
/// Manages the profile registry (profiles.json under the data root). Each
/// profile has fully separate storage; ephemeral (private/guest) profiles live
/// in a temp folder that is deleted when the last private window closes.
/// </summary>
public sealed class ProfileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProfileManager(AppPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
    }

    public AppPaths Paths => _paths;

    public async Task<IReadOnlyList<ProfileDescriptor>> GetProfilesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadIndexAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ProfileContext> GetOrCreateDefaultContextAsync(CancellationToken ct = default)
    {
        var profiles = await GetProfilesAsync(ct).ConfigureAwait(false);
        var descriptor = profiles.FirstOrDefault(p => p.Id == BrowserProfile.DefaultProfileId);
        if (descriptor is null)
        {
            descriptor = await CreateProfileAsync(BrowserProfile.DefaultProfileId, "Default", "#7C3AED", isGuest: false, ct).ConfigureAwait(false);
        }
        return ResolveContext(descriptor, isPrivate: false, ephemeralDirectory: null);
    }

    public async Task<ProfileDescriptor> CreateProfileAsync(string name, string colorHex, CancellationToken ct = default)
    {
        var id = "profile-" + Guid.NewGuid().ToString("N")[..12];
        return await CreateProfileAsync(id, name, colorHex, isGuest: false, ct).ConfigureAwait(false);
    }

    private async Task<ProfileDescriptor> CreateProfileAsync(string id, string name, string colorHex, bool isGuest, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var profiles = (await LoadIndexAsync(ct).ConfigureAwait(false)).ToList();
            var safeName = new string((name ?? "Profile").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (safeName.Length == 0) safeName = "Profile";
            if (safeName.Length > 60) safeName = safeName[..60];
            var descriptor = new ProfileDescriptor(id, safeName,
                IsHexColor(colorHex) ? colorHex : "#7C3AED", _clock.UtcNow, isGuest);
            profiles.RemoveAll(p => p.Id == id);
            profiles.Add(descriptor);
            await SaveIndexAsync(profiles, ct).ConfigureAwait(false);
            return descriptor;
        }
        finally
        {
            _lock.Release();
        }
    }

    public ProfileContext ResolveContext(ProfileDescriptor descriptor, bool isPrivate, string? ephemeralDirectory)
    {
        var profile = new BrowserProfile
        {
            Id = descriptor.Id,
            Name = descriptor.Name,
            ColorHex = descriptor.ColorHex,
            CreatedUtc = descriptor.CreatedUtc,
            IsGuest = descriptor.IsGuest,
            IsEphemeral = ephemeralDirectory is not null
        };

        if (ephemeralDirectory is not null)
        {
            Directory.CreateDirectory(ephemeralDirectory);
            var webView = Path.Combine(ephemeralDirectory, "WebView2");
            var logs = Path.Combine(ephemeralDirectory, "Logs");
            Directory.CreateDirectory(webView);
            Directory.CreateDirectory(logs);
            return new ProfileContext(
                profile,
                ephemeralDirectory,
                Path.Combine(ephemeralDirectory, "peyda-private.db"),
                webView,
                logs,
                isPrivate);
        }

        _paths.EnsureProfileDirectories(descriptor.Id);
        return new ProfileContext(
            profile,
            _paths.ProfileDirectory(descriptor.Id),
            _paths.ProfileDatabasePath(descriptor.Id),
            _paths.ProfileWebView2Directory(descriptor.Id),
            _paths.ProfileLogsDirectory(descriptor.Id),
            isPrivate);
    }

    /// <summary>Creates a private-browsing context that shares nothing with the real
    /// profile: its own temp database (so the same code paths run) and its own
    /// WebView2 user-data folder, both deleted on <see cref="DisposeEphemeralContext"/>.</summary>
    public ProfileContext CreatePrivateContext()
    {
        var descriptor = new ProfileDescriptor("private", "Private", "#4C1D95", _clock.UtcNow, IsGuest: false);
        return ResolveContext(descriptor, isPrivate: true, _paths.CreateEphemeralDirectory());
    }

    /// <summary>A fresh guest profile: like private mode, but presented as its own profile.</summary>
    public ProfileContext CreateGuestContext()
    {
        var descriptor = new ProfileDescriptor("guest", "Guest", "#0EA5E9", _clock.UtcNow, IsGuest: true);
        return ResolveContext(descriptor, isPrivate: true, _paths.CreateEphemeralDirectory());
    }

    public void DisposeEphemeralContext(ProfileContext context)
    {
        if (context.Profile.IsEphemeral)
            AppPaths.DeleteDirectorySafe(context.ProfileDirectory);
    }

    private async Task<IReadOnlyList<ProfileDescriptor>> LoadIndexAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_paths.ProfilesIndexPath)) return Array.Empty<ProfileDescriptor>();
            await using var stream = File.OpenRead(_paths.ProfilesIndexPath);
            var items = await JsonSerializer.DeserializeAsync<List<ProfileDescriptor>>(stream, JsonOptions, ct).ConfigureAwait(false);
            return items ?? new List<ProfileDescriptor>();
        }
        catch (JsonException)
        {
            // Corrupt index must not crash startup; it is regenerated with a Default profile.
            return Array.Empty<ProfileDescriptor>();
        }
    }

    private async Task SaveIndexAsync(IReadOnlyList<ProfileDescriptor> profiles, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        var tempPath = _paths.ProfilesIndexPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tempPath, _paths.ProfilesIndexPath, overwrite: true);
    }

    private static bool IsHexColor(string? value) =>
        value is not null && value.Length is 7 or 9 && value[0] == '#' &&
        value.Skip(1).All(Uri.IsHexDigit);
}
