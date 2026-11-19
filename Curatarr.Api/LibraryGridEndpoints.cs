using Curatarr.Engine.Data;
using Curatarr.Engine.LibraryGrid;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Paged library grid endpoints (Sprint 5 #8). Kept separate from
/// /api/source-items -- that endpoint stays untouched as the feature-flag
/// fallback. Both are readable by any authenticated user; annotations are
/// read-only metadata, so there is no new admin/membership write gate.
/// </summary>
public static class LibraryGridEndpoints
{
    private static readonly string[] AllowedViews = ["all", "library", "unmarked", "hidden"];

    public static void MapLibraryGridEndpoints(this WebApplication app)
    {
        app.MapGet("/api/library-entries", async (
            HttpContext http,
            CuratarrDbContext db,
            string? libraryId,
            string? mediaType,
            string? q,
            string? rating,
            string? studio,
            string? network,
            string? letter,
            string? view,
            bool? unmatched,
            int? offset,
            int? limit) =>
        {
            if (!TryParseMediaType(mediaType, out var kind, out var mediaError)) return Results.BadRequest(new { error = mediaError });
            if (!string.IsNullOrWhiteSpace(view) && !AllowedViews.Contains(view.Trim().ToLowerInvariant()))
                return Results.BadRequest(new { error = "view must be all, library, unmarked or hidden" });

            // Unknown library ids are a client error, never a 500 from the
            // annotation lookups below (research §8.6 #40).
            if (!string.IsNullOrWhiteSpace(libraryId) &&
                !await db.VirtualLibraries.AnyAsync(l => l.Id == libraryId))
                return Results.NotFound();

            var query = db.SourceItems.AsNoTracking();
            if (kind is not null)
            {
                var isMovie = kind == MediaKind.Movie;
                query = query.Where(i => i.MediaType == kind.Value &&
                    (isMovie
                        ? i.SeasonNumber == null && i.EpisodeNumber == null
                        : i.SeasonNumber != null || i.EpisodeNumber != null));
            }
            // Coarse SQL prefilters; the exact delimiter-aware predicates are
            // re-applied by LibraryEntriesQuery so semantics stay identical to
            // the unit-tested pure helpers.
            if (!string.IsNullOrWhiteSpace(rating)) query = query.Where(i => i.Rating == rating);
            if (!string.IsNullOrWhiteSpace(studio)) query = query.Where(i => i.ProductionCompanies != null && i.ProductionCompanies.Contains(studio));
            if (!string.IsNullOrWhiteSpace(network)) query = query.Where(i => i.Networks != null && i.Networks.Contains(network));
            if (unmatched == true) query = query.Where(i => i.TmdbId == null || i.TmdbId <= 0);

            var genres = ParseGenres(http);
            if (genres.Count > 0)
            {
                // Coarse prefilter only: the exact delimiter-aware, OR-across-
                // repeated-genre check lives in LibraryEntriesQuery.
                query = query.Where(i => i.GenreIds != null && i.GenreIds != "");
            }

            // 18-field projection -- the same columns the old /source-items
            // call transferred for every row, but here only the page is sent.
            var raw = await query.Select(i => new
            {
                i.Id, i.MediaType, i.Title, i.Year,
                i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
                i.TmdbId, i.PosterUrl, i.Overview, i.RelativePath,
                i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks,
                i.Director, i.Creators, i.Cast
            }).ToListAsync();

            var rows = raw.Select(i => new GridRow
            {
                Id = i.Id,
                MediaType = i.MediaType.ToString(),
                Title = i.Title,
                Year = i.Year,
                ShowTitle = i.ShowTitle,
                SeasonNumber = i.SeasonNumber,
                EpisodeNumber = i.EpisodeNumber,
                TmdbId = i.TmdbId,
                PosterUrl = i.PosterUrl,
                Overview = i.Overview,
                RelativePath = i.RelativePath,
                GenreIds = i.GenreIds,
                Rating = i.Rating,
                ProductionCompanies = i.ProductionCompanies,
                Networks = i.Networks,
                Director = i.Director,
                Creators = i.Creators,
                Cast = i.Cast,
            }).ToList();

            var annotations = await LoadAnnotationsAsync(db, libraryId);
            var page = LibraryEntriesQuery.Build(rows, new LibraryGridQuerySpec
            {
                MediaType = mediaType,
                Q = q,
                Genres = genres,
                Rating = rating,
                Studio = studio,
                Network = network,
                Letter = letter,
                IsUnmatched = unmatched == true,
                View = view ?? "all",
                Offset = offset,
                Limit = limit,
            }, annotations);

            return Results.Ok(page);
        }).RequireAuthorization();

        app.MapGet("/api/library-facets", async (
            HttpContext http,
            CuratarrDbContext db,
            string? libraryId,
            string? mediaType,
            string? view,
            bool? unmatched) =>
        {
            if (!TryParseMediaType(mediaType, out var kind, out var mediaError)) return Results.BadRequest(new { error = mediaError });
            if (!string.IsNullOrWhiteSpace(view) && !AllowedViews.Contains(view.Trim().ToLowerInvariant()))
                return Results.BadRequest(new { error = "view must be all, library, unmarked or hidden" });

            if (!string.IsNullOrWhiteSpace(libraryId) &&
                !await db.VirtualLibraries.AnyAsync(l => l.Id == libraryId))
                return Results.NotFound();

            var query = db.SourceItems.AsNoTracking();
            if (kind is not null)
            {
                var isMovie = kind == MediaKind.Movie;
                query = query.Where(i => i.MediaType == kind.Value &&
                    (isMovie
                        ? i.SeasonNumber == null && i.EpisodeNumber == null
                        : i.SeasonNumber != null || i.EpisodeNumber != null));
            }

            if (unmatched == true) query = query.Where(i => i.TmdbId == null || i.TmdbId <= 0);

            var raw = await query.Select(i => new
            {
                i.Id, i.MediaType, i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
                i.TmdbId, i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks
            }).ToListAsync();

            var rows = raw.Select(i => new GridRow
            {
                Id = i.Id,
                MediaType = i.MediaType.ToString(),
                ShowTitle = i.ShowTitle,
                SeasonNumber = i.SeasonNumber,
                EpisodeNumber = i.EpisodeNumber,
                TmdbId = i.TmdbId,
                GenreIds = i.GenreIds,
                Rating = i.Rating,
                ProductionCompanies = i.ProductionCompanies,
                Networks = i.Networks,
            }).ToList();

            var annotations = await LoadAnnotationsAsync(db, libraryId);
            return Results.Ok(LibraryFacetsQuery.Build(rows, mediaType, annotations, view, unmatched == true));
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

    private static List<int> ParseGenres(HttpContext http)
    {
        var result = new List<int>();
        foreach (var value in http.Request.Query["genre"])
        {
            foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && !result.Contains(id)) result.Add(id);
            }
        }
        return result;
    }

    private static async Task<AnnotationMap> LoadAnnotationsAsync(CuratarrDbContext db, string? libraryId)
    {
        if (string.IsNullOrWhiteSpace(libraryId)) return AnnotationMap.Empty;

        var approvals = await db.VirtualLibraryItems
            .Where(v => v.VirtualLibraryId == libraryId)
            .Select(v => new { v.SourceItemId, v.ApprovedAt })
            .ToListAsync();
        var approvedAtByItem = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var approval in approvals)
        {
            if (!approvedAtByItem.TryGetValue(approval.SourceItemId, out var existing) || approval.ApprovedAt > existing)
                approvedAtByItem[approval.SourceItemId] = approval.ApprovedAt;
        }

        // Hides are two-phase (Bug #4). Only hides already applied by a Sync,
        // plus unhide requests still waiting for one, are hidden in the grid.
        // HiddenAt always carries the current request stamp so the eye can show
        // yellow until that Sync and green/gray afterwards.
        var lastReconciledAt = await db.VirtualLibraries
            .Where(l => l.Id == libraryId)
            .Select(l => (DateTime?)l.LastReconciledAt)
            .FirstOrDefaultAsync();
        var hiddenRows = await db.HiddenItems
            .Where(h => h.VirtualLibraryId == libraryId)
            .Select(h => new { h.SourceItemId, h.CreatedAt, h.UnhideRequestedAt })
            .ToListAsync();
        var hiddenAtByItem = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var hiddenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in hiddenRows)
        {
            var stamp = HiddenItemState.StateStamp(row.CreatedAt, row.UnhideRequestedAt);
            if (!hiddenAtByItem.TryGetValue(row.SourceItemId, out var existing) || stamp > existing)
                hiddenAtByItem[row.SourceItemId] = stamp;
            if (HiddenItemState.IsEffectiveHidden(row.CreatedAt, row.UnhideRequestedAt, lastReconciledAt))
                hiddenIds.Add(row.SourceItemId);
        }

        return new AnnotationMap(approvedAtByItem, hiddenIds, hiddenAtByItem);
    }
}
