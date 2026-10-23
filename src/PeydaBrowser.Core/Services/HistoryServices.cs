using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;

namespace PeydaBrowser.Core.Services;

/// <summary>
/// Records and queries history. Private windows and profiles with history
/// recording disabled never write to the repository; internal peyda:// pages
/// and about:blank are not recorded either.
/// </summary>
public sealed class HistoryService
{
    private readonly IHistoryRepository _repository;
    private readonly IClock _clock;

    public HistoryService(IHistoryRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    /// <summary>Pure predicate used by both the app and tests.</summary>
    public static bool ShouldRecord(string? url, bool recordingEnabled, bool isPrivateContext)
    {
        if (!recordingEnabled || isPrivateContext) return false;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (PeydaUrl.IsPeydaUrl(url)) return false;
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
        return UrlValidator.IsValidHttpUrl(url, out _);
    }

    public async Task RecordVisitAsync(string url, string title, bool recordingEnabled, bool isPrivateContext,
        string? faviconUrl = null, CancellationToken ct = default)
    {
        if (!ShouldRecord(url, recordingEnabled, isPrivateContext)) return;
        await _repository.UpsertVisitAsync(url, title ?? string.Empty, _clock.UtcNow, faviconUrl, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit = 200, CancellationToken ct = default) =>
        _repository.GetRecentAsync(Math.Clamp(limit, 1, 2000), ct);

    public Task<IReadOnlyList<HistoryEntry>> SearchAsync(string query, int limit = 100, CancellationToken ct = default) =>
        _repository.SearchAsync(query ?? string.Empty, Math.Clamp(limit, 1, 2000), ct);

    public Task DeleteAsync(long id, CancellationToken ct = default) => _repository.DeleteAsync(id, ct);

    public Task ClearAsync(ClearTimeRange range, CancellationToken ct = default) =>
        _repository.DeleteRangeAsync(range.GetCutoffUtc(_clock.UtcNow), ct);
}

public enum HistoryBucket
{
    Today,
    Yesterday,
    Last7Days,
    Older
}

public sealed record HistoryGroup(HistoryBucket Bucket, IReadOnlyList<HistoryEntry> Entries);

public static class HistoryGrouper
{
    public static IReadOnlyList<HistoryGroup> Group(IEnumerable<HistoryEntry> entries, DateTimeOffset nowUtc, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var today = TimeZoneInfo.ConvertTime(nowUtc, tz).Date;

        var buckets = new Dictionary<HistoryBucket, List<HistoryEntry>>
        {
            [HistoryBucket.Today] = new(),
            [HistoryBucket.Yesterday] = new(),
            [HistoryBucket.Last7Days] = new(),
            [HistoryBucket.Older] = new()
        };

        foreach (var entry in entries.OrderByDescending(e => e.LastVisitUtc))
        {
            var day = TimeZoneInfo.ConvertTime(entry.LastVisitUtc, tz).Date;
            var delta = (today - day).TotalDays;
            var bucket = delta switch
            {
                <= 0 => HistoryBucket.Today,
                1 => HistoryBucket.Yesterday,
                <= 7 => HistoryBucket.Last7Days,
                _ => HistoryBucket.Older
            };
            buckets[bucket].Add(entry);
        }

        return buckets
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new HistoryGroup(kv.Key, kv.Value))
            .ToList();
    }
}
