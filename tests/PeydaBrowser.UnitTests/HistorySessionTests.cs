using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.UnitTests;

public class HistoryServiceTests
{
    [Theory]
    [InlineData("https://example.com", true, false, true)]
    [InlineData("https://example.com", true, true, false)]   // private window: never record
    [InlineData("https://example.com", false, false, false)] // user disabled history
    [InlineData("peyda://newtab", true, false, false)]       // internal page
    [InlineData("about:blank", true, false, false)]
    [InlineData("data:text/html,hi", true, false, false)]
    [InlineData("notaurl", true, false, false)]
    public void ShouldRecord_RespectsPrivacyRules(string url, bool enabled, bool isPrivate, bool expected)
    {
        Assert.Equal(expected, HistoryService.ShouldRecord(url, enabled, isPrivate));
    }

    [Fact]
    public void Grouper_SplitsIntoTodayYesterdayWeekOlder()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        HistoryEntry At(DateTimeOffset t) => new() { Url = "https://example.com/" + t.Ticks, LastVisitUtc = t, FirstVisitUtc = t };
        var entries = new[]
        {
            At(now.AddHours(-1)),
            At(now.AddDays(-1).AddHours(-2)),
            At(now.AddDays(-3)),
            At(now.AddDays(-30))
        };

        var groups = HistoryGrouper.Group(entries, now);

        Assert.Equal(4, groups.Count);
        Assert.Equal(HistoryBucket.Today, groups[0].Bucket);
        Assert.Equal(HistoryBucket.Yesterday, groups[1].Bucket);
        Assert.Equal(HistoryBucket.Last7Days, groups[2].Bucket);
        Assert.Equal(HistoryBucket.Older, groups[3].Bucket);
    }
}

public class SessionServiceTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("peyda://newtab", true)]
    [InlineData("peyda://private", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsRestorable_FiltersTransientUrls(string url, bool expected)
    {
        Assert.Equal(expected, SessionService.IsRestorable(url));
    }

    [Fact]
    public void PrivateSessions_AreNeverPersisted()
    {
        Assert.False(SessionService.ShouldPersist(isPrivateContext: true));
        Assert.True(SessionService.ShouldPersist(isPrivateContext: false));
    }
}

public class ClearTimeRangeTests
{
    [Fact]
    public void Cutoffs_AreComputedFromNow()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(now.AddHours(-1), ClearTimeRange.LastHour.GetCutoffUtc(now));
        Assert.Equal(now.AddHours(-24), ClearTimeRange.Last24Hours.GetCutoffUtc(now));
        Assert.Equal(now.AddDays(-7), ClearTimeRange.Last7Days.GetCutoffUtc(now));
        Assert.Equal(now.AddDays(-28), ClearTimeRange.Last4Weeks.GetCutoffUtc(now));
        Assert.Null(ClearTimeRange.AllTime.GetCutoffUtc(now));
    }
}
