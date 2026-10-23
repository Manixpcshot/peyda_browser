using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using PeydaBrowser.Core.Updates;

namespace PeydaBrowser.Infrastructure.Updates;

public sealed record UpdateCheckResult(bool Success, GitHubReleaseInfo? AvailableUpdate, SemanticVersion? AvailableVersion, string? ErrorMessage);

/// <summary>
/// Checks GitHub Releases for a newer Peyda build. Security posture for v1:
/// this service only <em>reports</em> an update and links to the official
/// release page — it never downloads or executes an installer by itself. The
/// user downloads manually and can verify SHA256SUMS.txt (see FileIntegrity).
/// </summary>
public sealed class GitHubUpdateService
{
    public const string RepositoryOwner = "Manixpcshot";
    public const string RepositoryName = "peyda_browser";

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repository;

    public GitHubUpdateService(HttpClient httpClient, string owner = RepositoryOwner, string repository = RepositoryName)
    {
        _httpClient = httpClient;
        _owner = owner;
        _repository = repository;
    }

    public string ReleasesPageUrl => $"https://github.com/{_owner}/{_repository}/releases";

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(SemanticVersion currentVersion, UpdateChannel channel, CancellationToken ct = default)
    {
        try
        {
            var releases = await GetReleasesAsync(ct).ConfigureAwait(false);
            var update = ReleaseSelector.SelectUpdate(releases, currentVersion, channel);
            SemanticVersion? updateVersion = null;
            if (update is not null && ReleaseSelector.TryExtractVersion(update, out var parsed))
                updateVersion = parsed;
            return new UpdateCheckResult(true, update, updateVersion, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateCheckResult(false, null, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{_owner}/{_repository}/releases?per_page=30");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PeydaBrowser", GetInformationalVersion()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var releases = new List<GitHubReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var assets = new List<GitHubAssetInfo>();
            if (item.TryGetProperty("assets", out var assetArray))
            {
                foreach (var asset in assetArray.EnumerateArray())
                {
                    assets.Add(new GitHubAssetInfo(
                        GetString(asset, "name"),
                        GetString(asset, "browser_download_url"),
                        asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0));
                }
            }

            releases.Add(new GitHubReleaseInfo(
                TagName: GetString(item, "tag_name"),
                Name: GetString(item, "name"),
                Body: GetString(item, "body"),
                HtmlUrl: GetString(item, "html_url"),
                IsDraft: item.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
                IsPrerelease: item.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean(),
                PublishedUtc: item.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String
                    ? published.GetDateTimeOffset()
                    : DateTimeOffset.MinValue,
                Assets: assets));
        }
        return releases;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Reads the product version from the entry assembly (set centrally in Directory.Build.props).</summary>
    public static string GetInformationalVersion(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }

    public static SemanticVersion GetCurrentVersion(Assembly? assembly = null) =>
        SemanticVersion.TryParse(GetInformationalVersion(assembly), out var version) ? version : SemanticVersion.Parse("0.0.0");
}
