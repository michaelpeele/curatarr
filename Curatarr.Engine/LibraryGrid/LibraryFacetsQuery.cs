namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// Pure facet computation for GET /api/library-facets. Mirrors the old
/// client-side option lists so dropdowns no longer need the full catalog.
/// Deliberately ignores q / letter / offset / limit (research §8.5 #36).
/// </summary>
public static class LibraryFacetsQuery
{
    public static readonly string[] MovieRatingOrder = ["G", "PG", "PG-13", "R", "NC-17", "NR", "Unrated"];
    public static readonly string[] TvRatingOrder = ["TV-Y", "TV-Y7", "TV-Y7-FV", "TV-G", "TV-PG", "TV-14", "TV-MA"];

    public static LibraryFacetsDto Build(
        IReadOnlyList<GridRow> rows,
        string? mediaType,
        AnnotationMap? annotations = null,
        string? view = null,
        bool isUnmatched = false)
    {
        annotations ??= AnnotationMap.Empty;
        var normalizedView = string.IsNullOrWhiteSpace(view) ? "all" : view.Trim().ToLowerInvariant();

        var visible = rows.Where(r => GridFilters.InMediaType(r, mediaType) &&
            (!isUnmatched || GridFilters.IsUnmatched(r)));
        visible = normalizedView == "hidden"
            ? visible.Where(r => annotations.HiddenIds.Contains(r.Id))
            : visible.Where(r => !annotations.HiddenIds.Contains(r.Id));
        var scope = visible.ToList();

        // Canonical order depends on the scope: a Movie library never offers
        // TV ratings, a Show library never offers movie ratings, browse offers
        // both. Anything unexpected is appended alphabetically.
        string[] order = mediaType switch
        {
            "Movie" => MovieRatingOrder,
            "Show" or "Anime" => TvRatingOrder,
            _ => MovieRatingOrder.Concat(TvRatingOrder).ToArray(),
        };
        var ratingsPresent = scope.Select(r => r.Rating)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .ToHashSet(StringComparer.Ordinal);
        var ratings = order.Where(r => ratingsPresent.Contains(r))
            .Concat(ratingsPresent.Where(r => !order.Contains(r)).OrderBy(r => r, StringComparer.Ordinal))
            .ToArray();

        var studios = scope.Where(r => GridFilters.InMediaType(r, "Movie"))
            .SelectMany(r => GridFilters.SplitPipe(r.ProductionCompanies))
            .ToHashSet(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var networks = scope.Where(r => !GridFilters.InMediaType(r, "Movie"))
            .SelectMany(r => GridFilters.SplitPipe(r.Networks))
            .ToHashSet(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var counts = new Dictionary<int, int>();
        foreach (var row in scope)
        {
            foreach (var raw in GridFilters.SplitGenres(row.GenreIds))
            {
                if (!int.TryParse(raw, out var id)) continue;
                counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;
            }
        }

        return new LibraryFacetsDto
        {
            Ratings = ratings,
            Studios = studios,
            Networks = networks,
            Genres = counts.OrderBy(kv => kv.Key)
                .Select(kv => new GenreFacetDto { Id = kv.Key, Count = kv.Value })
                .ToArray(),
        };
    }
}
