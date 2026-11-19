namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// Pure, DbContext-free implementation of GET /api/library-entries. The API
/// endpoint only validates query strings, loads the candidate rows, builds an
/// <see cref="AnnotationMap"/> and hands both to this class -- so paging,
/// filtering, grouping and annotation are all directly unit-testable.
/// </summary>
public static class LibraryEntriesQuery
{
    /// <summary>episodeIds is capped purely so the details modal can resolve one episode.</summary>
    public const int EpisodeIdLimit = 200;

    private sealed class Candidate
    {
        public required LibraryEntryDto Entry { get; init; }
        public required string Title { get; init; }
        public required string SortKey { get; init; }
        public required string Haystack { get; init; }
        public required IReadOnlyList<string> Ids { get; init; }
    }

    public static IReadOnlyList<string> Tokenize(string? q) =>
        (q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static LibraryEntriesPageDto Build(
        IReadOnlyList<GridRow> rows,
        LibraryGridQuerySpec spec,
        AnnotationMap? annotations = null)
    {
        annotations ??= AnnotationMap.Empty;
        var offset = GridPaging.NormalizeOffset(spec.Offset);
        var limit = GridPaging.ClampLimit(spec.Limit);
        var tokens = Tokenize(spec.Q);
        var letter = string.IsNullOrWhiteSpace(spec.Letter) ? null : spec.Letter.Trim().ToUpperInvariant();
        var genres = spec.Genres ?? Array.Empty<int>();

        var matches = rows.Where(row =>
                GridFilters.InMediaType(row, spec.MediaType) &&
                (!spec.IsUnmatched || GridFilters.IsUnmatched(row)) &&
                (genres.Count == 0 || genres.Any(id => GridFilters.MatchesGenre(row.GenreIds, id))) &&
                (string.IsNullOrWhiteSpace(spec.Rating) || GridFilters.MatchesRating(row.Rating, spec.Rating)) &&
                (string.IsNullOrWhiteSpace(spec.Studio) || GridFilters.MatchesStudio(row.ProductionCompanies, spec.Studio)) &&
                (string.IsNullOrWhiteSpace(spec.Network) || GridFilters.MatchesNetwork(row.Networks, spec.Network)))
            .ToList();

        var candidates = new List<Candidate>();

        foreach (var row in matches.Where(r => GridFilters.InMediaType(r, "Movie")))
        {
            var title = row.Title ?? "Untitled";
            candidates.Add(new Candidate
            {
                Entry = new LibraryEntryDto
                {
                    Kind = "movie",
                    Id = row.Id,
                    Title = title,
                    MediaType = "Movie",
                    PosterUrl = row.PosterUrl,
                    Overview = row.Overview,
                    Year = row.Year,
                    TmdbId = row.TmdbId,
                    RelativePath = row.RelativePath,
                    GenreIds = GridFilters.SplitGenres(row.GenreIds)
                        .Select(g => int.TryParse(g, out var id) ? id : 0)
                        .Where(id => id != 0).ToArray(),
                    Rating = row.Rating,
                    ProductionCompanies = row.ProductionCompanies,
                    Director = row.Director,
                    Cast = row.Cast,
                },
                Title = title,
                SortKey = row.Id,
                Haystack = string.Join(" ", title, row.Director ?? "", row.Cast ?? ""),
                Ids = new[] { row.Id },
            });
        }

        var showRows = matches.Where(r => !GridFilters.InMediaType(r, "Movie")).ToList();
        foreach (var group in ShowGrouping.BuildGroups(showRows))
        {
            var ids = group.EpisodeIds.Take(EpisodeIdLimit).ToArray();
            candidates.Add(new Candidate
            {
                Entry = new LibraryEntryDto
                {
                    Kind = "show",
                    Key = group.Key,
                    Title = group.Title,
                    MediaType = group.MediaType,
                    PosterUrl = group.PosterUrl,
                    Overview = group.Overview,
                    TmdbId = group.TmdbId,
                    GenreIds = GridFilters.SplitGenres(group.GenreIds)
                        .Select(g => int.TryParse(g, out var id) ? id : 0)
                        .Where(id => id != 0).ToArray(),
                    Rating = group.Rating,
                    Networks = group.Networks,
                    Creators = group.Creators,
                    Cast = group.Cast,
                    SeasonCount = group.SeasonCount,
                    EpisodeCount = group.EpisodeCount,
                    LatestSeason = group.LatestSeason,
                    LatestEpisode = group.LatestEpisode,
                    RepresentativeId = ShowGrouping.RepresentativeId(group),
                    EpisodeIds = ids,
                },
                Title = group.Title,
                SortKey = group.Key,
                Haystack = string.Join(" ", group.Title, group.Creators ?? "", group.Cast ?? ""),
                Ids = ids,
            });
        }

        var filtered = candidates
            .Where(c => GridFilters.MatchesTokens(c.Haystack, tokens))
            .Where(c => letter is null || GridFilters.FirstLetter(c.Title) == letter)
            .ToList();

        // Annotations are decided once per group: approved when ANY episode is
        // approved (and approvedAt is the newest stamp), hidden only when ALL
        // episodes are hidden, and hiddenAt is the newest hide stamp among the
        // group's episode ids. This is the decision recorded in the research
        // doc §7.2 and surfaced in the card tooltip.
        foreach (var candidate in filtered)
        {
            DateTime? newest = null;
            var allHidden = candidate.Ids.Count > 0;
            foreach (var id in candidate.Ids)
            {
                if (annotations.ApprovedAtByItem.TryGetValue(id, out var at) && (newest is null || at > newest.Value))
                    newest = at;
                if (!annotations.HiddenIds.Contains(id)) allHidden = false;
            }
            candidate.Entry.Approved = newest is not null;
            candidate.Entry.ApprovedAt = newest;
            candidate.Entry.Hidden = allHidden;
            candidate.Entry.HiddenAt = annotations.NewestHiddenAt(candidate.Ids);
        }

        var view = string.IsNullOrWhiteSpace(spec.View) ? "all" : spec.View.Trim().ToLowerInvariant();
        var visible = view switch
        {
            "hidden" => filtered.Where(c => c.Entry.Hidden).ToList(),
            "library" => filtered.Where(c => !c.Entry.Hidden && c.Entry.Approved).ToList(),
            "unmarked" => filtered.Where(c => !c.Entry.Hidden && !c.Entry.Approved).ToList(),
            _ => filtered.Where(c => !c.Entry.Hidden).ToList(), // "all"
        };

        visible.Sort((a, b) =>
        {
            var byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            return byTitle != 0 ? byTitle : string.Compare(a.SortKey, b.SortKey, StringComparison.Ordinal);
        });

        var total = visible.Count;
        var page = GridPaging.Page(visible, offset, limit);
        return new LibraryEntriesPageDto
        {
            Total = total,
            Offset = offset,
            Limit = limit,
            HasMore = GridPaging.HasMore(offset, page.Count, total),
            MediaType = spec.MediaType,
            Entries = page.Select(c => c.Entry).ToList(),
        };
    }
}
