using System.Globalization;
using System.Security.Claims;
using Curatarr.Engine.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Read-only Calendar data (Sprint 4 #3, slice 1). Dates come from SourceItem
/// columns parsed out of cached TmdbRawDetails.Json; this endpoint makes no
/// TMDB calls. Any authenticated user may read it. When a library is selected,
/// the same read-style membership gate as the other library endpoints applies
/// and effectively hidden items are excluded.
/// </summary>
public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this WebApplication app)
    {
        app.MapGet("/api/calendar/events", async (
            ClaimsPrincipal principal,
            CuratarrDbContext db,
            ScheduleTimeZone timeZone,
            UserManager<ApplicationUser> userManager,
            CancellationToken ct,
            string? month,
            string? libraryId,
            string? mediaType) =>
        {
            if (!TryParseMediaType(mediaType, out var kind, out var mediaError))
                return Results.BadRequest(new { error = mediaError });

            // "Today" and the default month follow the configured schedule
            // timezone, not the browser's or the container's UTC clock.
            var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone.Zone).DateTime.Date;
            DateTime monthStart;
            if (string.IsNullOrWhiteSpace(month))
            {
                monthStart = new DateTime(today.Year, today.Month, 1);
            }
            else if (!DateTime.TryParseExact(
                month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out monthStart))
            {
                return Results.BadRequest(new { error = "month must be yyyy-MM" });
            }
            else
            {
                monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);
            }

            var monthEnd = monthStart.AddMonths(1);
            var normalizedLibraryId = string.IsNullOrWhiteSpace(libraryId) ? null : libraryId.Trim();

            if (normalizedLibraryId is not null)
            {
                if (!await db.VirtualLibraries.AnyAsync(l => l.Id == normalizedLibraryId, ct))
                    return Results.NotFound();

                if (!principal.IsInRole("Admin"))
                {
                    var user = await userManager.GetUserAsync(principal);
                    if (user is null) return Results.Unauthorized();
                    if (!await db.VirtualLibraryMemberships.AnyAsync(
                        m => m.VirtualLibraryId == normalizedLibraryId && m.UserId == user.Id, ct))
                        return Results.Forbid();
                }
            }

            var query = db.SourceItems.AsNoTracking().Where(i => i.TmdbId != null &&
                ((i.MediaType == MediaKind.Movie &&
                    i.ReleaseDate >= monthStart && i.ReleaseDate < monthEnd) ||
                 ((i.MediaType == MediaKind.Show || i.MediaType == MediaKind.Anime) &&
                    i.AirDate >= monthStart && i.AirDate < monthEnd)));

            if (kind is not null)
                query = query.Where(i => i.MediaType == kind.Value);

            HashSet<string>? approvedIds = null;
            HashSet<string>? hiddenIds = null;
            if (normalizedLibraryId is not null)
            {
                approvedIds = (await db.VirtualLibraryItems
                    .Where(v => v.VirtualLibraryId == normalizedLibraryId)
                    .Select(v => v.SourceItemId)
                    .ToListAsync(ct))
                    .ToHashSet(StringComparer.Ordinal);

                var lastReconciledAt = await db.VirtualLibraries
                    .Where(l => l.Id == normalizedLibraryId)
                    .Select(l => (DateTime?)l.LastReconciledAt)
                    .FirstOrDefaultAsync(ct);

                var hiddenRows = await db.HiddenItems
                    .Where(h => h.VirtualLibraryId == normalizedLibraryId)
                    .Select(h => new { h.SourceItemId, h.CreatedAt, h.UnhideRequestedAt })
                    .ToListAsync(ct);
                hiddenIds = hiddenRows
                    .Where(h => HiddenItemState.IsEffectiveHidden(h.CreatedAt, h.UnhideRequestedAt, lastReconciledAt))
                    .Select(h => h.SourceItemId)
                    .ToHashSet(StringComparer.Ordinal);

                query = query.Where(i => approvedIds.Contains(i.Id) && !hiddenIds.Contains(i.Id));
            }

            var rows = await query.Select(i => new
            {
                i.Id,
                i.MediaType,
                i.Title,
                i.ShowTitle,
                i.TmdbId,
                i.PosterUrl,
                i.ReleaseDate,
                i.AirDate,
                i.AirSeasonNumber,
                i.AirEpisodeNumber,
                i.AirIsSeasonPremiere
            }).ToListAsync(ct);

            // Every episode row of a show carries the same next_episode_to_air
            // value. Keep the first row that has season/episode metadata so the
            // badge survives de-duplication.
            var candidates = rows
                .OrderBy(r => r.MediaType == MediaKind.Movie ? r.ReleaseDate : r.AirDate)
                .ThenByDescending(r => r.MediaType != MediaKind.Movie &&
                    (r.AirSeasonNumber.HasValue || r.AirEpisodeNumber.HasValue))
                .ThenBy(r => r.MediaType == MediaKind.Movie ? r.Title : r.ShowTitle ?? r.Title)
                .ThenBy(r => r.Id)
                .ToList();

            // A de-duplicated show event can represent several locally stored
            // episode rows. Collect every SourceItem id for that event so the
            // slice-2 modal can hide them all. Iterating the ordered candidates
            // keeps the representative (the row with season/episode metadata)
            // first in each list.
            var showSourceItemIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var row in candidates)
            {
                if (row.MediaType == MediaKind.Movie) continue;
                var date = row.AirDate!.Value;
                var showKey = $"{row.MediaType}:{row.TmdbId}:{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
                if (!showSourceItemIds.TryGetValue(showKey, out var ids))
                {
                    ids = new List<string>();
                    showSourceItemIds.Add(showKey, ids);
                }
                ids.Add(row.Id);
            }

            var seenShows = new HashSet<string>(StringComparer.Ordinal);
            var events = new List<object>();
            foreach (var row in candidates)
            {
                var date = (row.MediaType == MediaKind.Movie ? row.ReleaseDate : row.AirDate)!.Value;
                var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string[] sourceItemIds;
                string? representativeSourceItemId;
                if (row.MediaType == MediaKind.Movie)
                {
                    sourceItemIds = new[] { row.Id };
                    representativeSourceItemId = row.Id;
                }
                else
                {
                    var showKey = $"{row.MediaType}:{row.TmdbId}:{dateText}";
                    if (!seenShows.Add(showKey)) continue;
                    sourceItemIds = showSourceItemIds[showKey].ToArray();
                    representativeSourceItemId = sourceItemIds.FirstOrDefault();
                }

                events.Add(new
                {
                    id = row.Id,
                    mediaType = row.MediaType,
                    title = row.MediaType == MediaKind.Movie
                        ? row.Title ?? "Untitled"
                        : row.ShowTitle ?? row.Title ?? "Untitled",
                    showTitle = row.MediaType == MediaKind.Movie ? null : row.ShowTitle ?? row.Title,
                    date = dateText,
                    posterUrl = row.PosterUrl,
                    seasonNumber = row.MediaType == MediaKind.Movie ? null : row.AirSeasonNumber,
                    episodeNumber = row.MediaType == MediaKind.Movie ? null : row.AirEpisodeNumber,
                    isSeasonPremiere = row.MediaType != MediaKind.Movie && row.AirIsSeasonPremiere,
                    tmdbId = row.TmdbId,
                    sourceItemIds,
                    representativeSourceItemId
                });
            }

            return Results.Ok(new
            {
                timeZone = timeZone.Id,
                today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                month = monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                events
            });
        }).RequireAuthorization();
    }

    private static bool TryParseMediaType(string? value, out MediaKind? kind, out string error)
    {
        kind = null;
        error = "";
        if (string.IsNullOrWhiteSpace(value)) return true;

        var text = value.Trim();
        if (!string.Equals(text, nameof(MediaKind.Movie), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(text, nameof(MediaKind.Show), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(text, nameof(MediaKind.Anime), StringComparison.OrdinalIgnoreCase))
        {
            error = "mediaType must be Movie, Show or Anime";
            return false;
        }
        kind = Enum.Parse<MediaKind>(text, ignoreCase: true);
        return true;
    }
}
