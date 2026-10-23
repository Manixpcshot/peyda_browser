using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.Core.Services;

/// <summary>
/// Session persistence (open tabs + recently closed tabs). Private windows
/// must never call the save/push methods; the app enforces this and unit
/// tests cover <see cref="ShouldPersist"/>.
/// </summary>
public sealed class SessionService
{
    public const int MaxClosedTabs = 25;
    public const int MaxRestoredTabs = 64;

    private readonly ISessionRepository _repository;
    private readonly IClock _clock;

    public SessionService(ISessionRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public static bool ShouldPersist(bool isPrivateContext) => !isPrivateContext;

    /// <summary>Only restorable tabs are stored: real web pages plus internal pages
    /// other than transient ones (private landing page). about:blank is dropped.</summary>
    public static bool IsRestorable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase)) return false;
        if (PeydaUrl.IsPeydaUrl(url))
            return PeydaUrl.GetPageName(url) != "private";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public async Task SaveAsync(IEnumerable<SessionTab> tabs, bool isPrivateContext, CancellationToken ct = default)
    {
        if (!ShouldPersist(isPrivateContext)) return;
        var restorable = tabs.Where(t => IsRestorable(t.Url)).Take(MaxRestoredTabs).ToList();
        var snapshot = new SessionSnapshot
        {
            Id = "auto",
            Name = "auto",
            CreatedUtc = _clock.UtcNow,
            Tabs = restorable
        };
        await _repository.SaveAutoSessionAsync(snapshot, ct).ConfigureAwait(false);
    }

    public Task<SessionSnapshot?> LoadAsync(CancellationToken ct = default) =>
        _repository.GetAutoSessionAsync(ct);

    public Task ClearAsync(CancellationToken ct = default) => _repository.ClearAutoSessionAsync(ct);

    public async Task PushClosedTabAsync(SessionTab tab, bool isPrivateContext, CancellationToken ct = default)
    {
        if (!ShouldPersist(isPrivateContext) || !IsRestorable(tab.Url)) return;
        await _repository.PushClosedTabAsync(new ClosedTab
        {
            Url = tab.Url,
            Title = tab.Title,
            IsPinned = tab.IsPinned,
            IsMuted = tab.IsMuted,
            ClosedUtc = _clock.UtcNow
        }, MaxClosedTabs, ct).ConfigureAwait(false);
    }

    public Task<ClosedTab?> PopClosedTabAsync(CancellationToken ct = default) =>
        _repository.PopClosedTabAsync(ct);
}
