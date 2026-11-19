using Curatarr.Api;
using Curatarr.Engine.Data;
using Xunit;

namespace Curatarr.Tests;

public class ActivityEndpointsTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static ActivityEvent Row(
        string id,
        DateTimeOffset occurredAt,
        ActivityCategory category = ActivityCategory.User,
        string type = "UserUpdated") => new()
    {
        Id = id,
        OccurredAt = occurredAt,
        Category = category,
        Type = type,
        Actor = "admin@example.com",
        Detail = "detail",
    };

    [Fact]
    public void Cursor_round_trips_ticks_and_id_including_equal_timestamps()
    {
        var a = Row("id-a", BaseTime);
        var b = Row("id-b", BaseTime); // identical timestamp, different id

        var encodedA = ActivityEndpoints.EncodeCursor(a);
        var encodedB = ActivityEndpoints.EncodeCursor(b);
        Assert.NotEqual(encodedA, encodedB);

        Assert.True(ActivityEndpoints.TryDecodeCursor(encodedA, out var decodedA));
        Assert.Equal(a.OccurredAt.UtcTicks, decodedA.Ticks);
        Assert.Equal("id-a", decodedA.Id);

        Assert.True(ActivityEndpoints.TryDecodeCursor(encodedB, out var decodedB));
        Assert.Equal(b.OccurredAt.UtcTicks, decodedB.Ticks);
        Assert.Equal("id-b", decodedB.Id);

        // Re-encoding the decoded position reproduces the original cursor.
        Assert.Equal(encodedA, ActivityEndpoints.EncodeCursor(decodedA.Ticks, decodedA.Id));

        Assert.False(ActivityEndpoints.TryDecodeCursor(null, out _));
        Assert.False(ActivityEndpoints.TryDecodeCursor("  ", out _));
        Assert.False(ActivityEndpoints.TryDecodeCursor("YWJj", out _)); // base64 of "abc": no separator
        Assert.False(ActivityEndpoints.TryDecodeCursor("not-base64!!", out _));
    }

    [Fact]
    public void FilterAndPage_orders_newest_first_with_id_tie_break()
    {
        var later = BaseTime.AddMinutes(1);
        var rows = new[]
        {
            Row("id-1", BaseTime),
            Row("id-3", later),
            Row("id-2", BaseTime),
        };

        var page = ActivityEndpoints.FilterAndPage(rows, null, null, null, 10);
        Assert.Equal(new[] { "id-3", "id-2", "id-1" }, page.Items.Select(e => e.Id));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void Next_cursor_marks_the_last_returned_row_and_paginates()
    {
        var rows = Enumerable.Range(1, 6)
            .Select(i => Row($"id-{i}", BaseTime.AddMinutes(i)))
            .ToList();

        var page1 = ActivityEndpoints.FilterAndPage(rows, null, null, null, 2);
        Assert.Equal(new[] { "id-6", "id-5" }, page1.Items.Select(e => e.Id));
        Assert.NotNull(page1.NextCursor);
        Assert.True(ActivityEndpoints.TryDecodeCursor(page1.NextCursor, out var decoded));
        Assert.Equal("id-5", decoded.Id);

        var page2 = ActivityEndpoints.FilterAndPage(rows, null, null, page1.NextCursor, 2);
        Assert.Equal(new[] { "id-4", "id-3" }, page2.Items.Select(e => e.Id));
        Assert.NotNull(page2.NextCursor);

        var page3 = ActivityEndpoints.FilterAndPage(rows, null, null, page2.NextCursor, 2);
        Assert.Equal(new[] { "id-2", "id-1" }, page3.Items.Select(e => e.Id));
        Assert.Null(page3.NextCursor);
    }

    [Fact]
    public void Cursor_pages_correctly_when_timestamps_are_equal()
    {
        var rows = new[] { Row("a", BaseTime), Row("b", BaseTime), Row("c", BaseTime) };

        var page1 = ActivityEndpoints.FilterAndPage(rows, null, null, null, 2);
        Assert.Equal(new[] { "c", "b" }, page1.Items.Select(e => e.Id));
        Assert.NotNull(page1.NextCursor);

        var page2 = ActivityEndpoints.FilterAndPage(rows, null, null, page1.NextCursor, 2);
        Assert.Equal(new[] { "a" }, page2.Items.Select(e => e.Id));
        Assert.Null(page2.NextCursor);
    }

    [Fact]
    public void Category_and_type_filters_are_applied_before_paging()
    {
        var rows = new[]
        {
            Row("scan-1", BaseTime.AddMinutes(5), ActivityCategory.Scan, "ScanStarted"),
            Row("user-1", BaseTime.AddMinutes(4), ActivityCategory.User, "UserLoggedIn"),
            Row("scan-2", BaseTime.AddMinutes(3), ActivityCategory.Scan, "ScanFinished"),
            Row("user-2", BaseTime.AddMinutes(2), ActivityCategory.User, "UserUpdated"),
        };

        var scans = ActivityEndpoints.FilterAndPage(rows, "scan", null, null, 10);
        Assert.Equal(new[] { "scan-1", "scan-2" }, scans.Items.Select(e => e.Id));

        var started = ActivityEndpoints.FilterAndPage(rows, null, "ScanStarted", null, 10);
        Assert.Equal("scan-1", Assert.Single(started.Items).Id);

        // With a limit of one, filtering first means the newest Scan (not the
        // globally newest User row) is what gets returned.
        var firstScan = ActivityEndpoints.FilterAndPage(rows, "Scan", null, null, 1);
        Assert.Equal("scan-1", Assert.Single(firstScan.Items).Id);
        Assert.NotNull(firstScan.NextCursor);

        // Both filters are case-insensitive and compose.
        var both = ActivityEndpoints.FilterAndPage(rows, "ScAn", "scanstarted", null, 10);
        Assert.Equal("scan-1", Assert.Single(both.Items).Id);
    }
}
