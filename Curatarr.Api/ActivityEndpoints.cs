using System.Text;
using System.Text.Json;
using Curatarr.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Sprint 5 #1 — Activity endpoints. Admin-only. The activity feed is an
/// append-only audit log, newest first, paged with an opaque cursor. Keeping
/// the filter/page logic in pure static helpers makes it unit-testable without
/// a database.
/// </summary>
public static class ActivityEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Paging/filter cursor: the position of the last row on a page.</summary>
    public sealed record ActivityCursor(long Ticks, string Id);

    /// <summary>Encode a row's position as an opaque base64url token.</summary>
    public static string EncodeCursor(ActivityEvent row) =>
        EncodeCursor(row.OccurredAt.UtcTicks, row.Id);

    public static string EncodeCursor(long ticks, string id) =>
        Base64UrlEncode($"{ticks}|{id}");

    /// <summary>Decode a cursor; returns false for null/malformed tokens.</summary>
    public static bool TryDecodeCursor(string? cursor, out ActivityCursor result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var raw = Base64UrlDecode(cursor);
            var sep = raw.IndexOf('|');
            if (sep <= 0) return false;
            if (!long.TryParse(raw[..sep], out var ticks)) return false;
            var id = raw[(sep + 1)..];
            if (id.Length == 0) return false;
            result = new ActivityCursor(ticks, id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pure filter + cursor page over a list. Newest first, tie-broken by Id
    /// descending so equal timestamps still page deterministically.
    /// </summary>
    public static ActivityPage FilterAndPage(
        IEnumerable<ActivityEvent> source,
        string? category,
        string? type,
        string? before,
        int limit)
    {
        var rows = source.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<ActivityCategory>(category, ignoreCase: true, out var cat))
            rows = rows.Where(e => e.Category == cat);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var needle = type.Trim();
            rows = rows.Where(e => string.Equals(e.Type, needle, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = rows
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id, StringComparer.Ordinal)
            .ToList();

        var take = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);

        if (TryDecodeCursor(before, out var cursor))
        {
            ordered = ordered
                .Where(e => e.OccurredAt.UtcTicks < cursor.Ticks ||
                            (e.OccurredAt.UtcTicks == cursor.Ticks &&
                             string.CompareOrdinal(e.Id, cursor.Id) < 0))
                .ToList();
        }

        var page = ordered.Take(take).ToList();
        string? nextCursor = ordered.Count > take && page.Count > 0
            ? EncodeCursor(page[^1])
            : null;

        return new ActivityPage(page, nextCursor);
    }

    public sealed record ActivityPage(IReadOnlyList<ActivityEvent> Items, string? NextCursor);

    public static void MapActivityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/activity", async (
            HttpContext http,
            CuratarrDbContext db,
            string? category,
            string? type,
            string? before,
            int? limit) =>
        {
            // SQLite cannot ORDER BY a DateTimeOffset column (nor its UtcTicks
            // navigation), so load the rows and let FilterAndPage do the
            // newest-first ordering and equal-timestamp tie-break in memory.
            // Activity is a small audit log (one row per user action), so a
            // full read is fine here.
            var rows = await db.ActivityEvents
                .AsNoTracking()
                .ToListAsync();

            var page = FilterAndPage(rows, category, type, before, limit ?? DefaultLimit);
            return Results.Ok(new { items = page.Items, nextCursor = page.NextCursor });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/activity/scan-status", (ScanControlService scan) =>
            Results.Ok(scan.Snapshot())
        ).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static string Base64UrlEncode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
