using System.Text.RegularExpressions;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Core.Services;

/// <summary>Builds and validates search URLs from <c>{query}</c> templates.
/// Queries are always URL-encoded before substitution.</summary>
public static class SearchUrlBuilder
{
    public const string QueryPlaceholder = "{query}";

    public static string BuildSearchUrl(SearchEngine engine, string query)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(query);
        if (!engine.UrlTemplate.Contains(QueryPlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException($"Search template for '{engine.Name}' does not contain {QueryPlaceholder}.");
        return engine.UrlTemplate.Replace(QueryPlaceholder, Uri.EscapeDataString(query), StringComparison.Ordinal);
    }

    /// <summary>Validates a user-supplied template. Returns a human-readable error key, or null when valid.</summary>
    public static string? ValidateTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "error.template.empty";
        if (template.Length > 2048) return "error.template.tooLong";
        if (template.Any(char.IsControl)) return "error.template.controlChars";
        if (!template.Contains(QueryPlaceholder, StringComparison.Ordinal)) return "error.template.missingQuery";
        var probe = template.Replace(QueryPlaceholder, "test", StringComparison.Ordinal);
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri)) return "error.template.invalidUrl";
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return "error.template.invalidScheme";
        if (string.IsNullOrWhiteSpace(uri.Host)) return "error.template.invalidUrl";
        return null;
    }

    public static bool IsSecureTemplate(string template) =>
        Uri.TryCreate(template.Replace(QueryPlaceholder, "x", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>CRUD + validation for search engines. Storage is delegated to
/// <see cref="ISearchEngineRepository"/> so the service is fully unit-testable.</summary>
public sealed class SearchEngineService
{
    private static readonly Regex BangRegex = new("^[a-z0-9]{1,16}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ISearchEngineRepository _repository;
    private readonly IClock _clock;

    public SearchEngineService(ISearchEngineRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public Task<IReadOnlyList<SearchEngine>> GetAllAsync(CancellationToken ct = default) =>
        _repository.GetAllAsync(ct);

    /// <summary>Seeds Google, Brave Search and DuckDuckGo when the table is empty (first run).</summary>
    public async Task EnsureBuiltInsAsync(CancellationToken ct = default)
    {
        var existing = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0) return;
        foreach (var engine in SearchEngine.BuiltIn.CreateDefaults(_clock.UtcNow))
            await _repository.UpsertAsync(engine, ct).ConfigureAwait(false);
    }

    /// <returns>Null on success; otherwise a localization error key.</returns>
    public async Task<string?> SaveAsync(SearchEngine engine, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (string.IsNullOrWhiteSpace(engine.Name) || engine.Name.Trim().Length > 60) return "error.engine.name";
        var templateError = SearchUrlBuilder.ValidateTemplate(engine.UrlTemplate);
        if (templateError is not null) return templateError;
        if (!string.IsNullOrWhiteSpace(engine.Bang))
        {
            engine.Bang = engine.Bang.Trim().TrimStart('!').ToLowerInvariant();
            if (!BangRegex.IsMatch(engine.Bang)) return "error.engine.bang";
            var others = await _repository.GetAllAsync(ct).ConfigureAwait(false);
            if (others.Any(o => o.Id != engine.Id && string.Equals(o.Bang, engine.Bang, StringComparison.OrdinalIgnoreCase)))
                return "error.engine.bangDuplicate";
        }
        await _repository.UpsertAsync(engine, ct).ConfigureAwait(false);
        return null;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        if (all.Count <= 1)
            throw new InvalidOperationException("At least one search engine must remain.");
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<SearchEngine> ResolveAsync(string? preferredId, string defaultEngineId, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        if (all.Count == 0) throw new InvalidOperationException("No search engines configured.");
        return all.FirstOrDefault(e => e.Id == preferredId)
               ?? all.FirstOrDefault(e => e.Id == defaultEngineId)
               ?? all[0];
    }

    public async Task<IReadOnlyDictionary<string, string>> GetBangMapAsync(CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var engine in all)
        {
            if (!string.IsNullOrWhiteSpace(engine.Bang))
                map[engine.Bang.Trim().ToLowerInvariant()] = engine.Id;
        }
        return map;
    }

    public Task SetSortOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default) =>
        _repository.SetSortOrderAsync(orderedIds, ct);

    public static string NewId() => "engine-" + Guid.NewGuid().ToString("N")[..12];
}
