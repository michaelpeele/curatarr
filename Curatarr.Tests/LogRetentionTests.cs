using Curatarr.Api;
using Xunit;

namespace Curatarr.Tests;

public class LogRetentionTests
{
    private static readonly DateTime TodayUtc = new(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Archive_candidates_exclude_today_and_unparseable_names()
    {
        var paths = new[]
        {
            "/logs/curatarr-2026-11-17.log",     // today -> excluded
            "/logs/curatarr-2026-11-16.log",     // yesterday
            "/logs/curatarr-2026-11-16_001.log", // same day, grouped above
            "/logs/curatarr-2026-11-15.txt",     // date is what matters, not extension
            "/logs/curatarr-2026-11-14.log",
            "/logs/other-2026-11-13.log",        // wrong prefix -> excluded
            "/logs/curatarr-bad.log",            // no date -> excluded
        };

        var groups = LogRetention.ArchiveCandidates(paths, TodayUtc);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new DateTime(2026, 11, 14), groups[0].Key);
        Assert.Equal(new DateTime(2026, 11, 15), groups[1].Key);
        Assert.Equal(new DateTime(2026, 11, 16), groups[2].Key);
        Assert.Equal(2, groups[2].Count());
        Assert.DoesNotContain(groups, g => g.Key == TodayUtc.Date);
        Assert.All(groups.SelectMany(g => g), p => Assert.StartsWith(LogRetention.FilePrefix, Path.GetFileName(p)));
    }

    [Fact]
    public void TryParseDate_accepts_only_prefixed_date_names()
    {
        Assert.True(LogRetention.TryParseDate("curatarr-2026-11-16.log", out var date));
        Assert.Equal(new DateTime(2026, 11, 16), date);
        Assert.True(LogRetention.TryParseDate("/logs/curatarr-2026-11-16_001.log", out date));
        Assert.Equal(new DateTime(2026, 11, 16), date);

        Assert.False(LogRetention.TryParseDate("other-2026-11-16.log", out _));
        Assert.False(LogRetention.TryParseDate("curatarr-2026-13-45.log", out _));
        Assert.False(LogRetention.TryParseDate("curatarr-bad.log", out _));
    }

    [Fact]
    public void IsExpired_boundary_is_exactly_the_cutoff()
    {
        var now = TodayUtc.AddHours(10);
        var cutoff = now.Date.AddDays(-7);
        Assert.Equal(new DateTime(2026, 11, 10), cutoff);

        Assert.False(LogRetention.IsExpired(cutoff, now, 7));           // exactly the cutoff
        Assert.True(LogRetention.IsExpired(cutoff.AddDays(-1), now, 7)); // one day older
        Assert.False(LogRetention.IsExpired(cutoff.AddDays(1), now, 7)); // still within window
    }
}
