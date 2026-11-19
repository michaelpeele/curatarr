namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// Server-side port of Curatarr.Web/src/lib/showGroups.ts. Shows are not a
/// database entity: one SourceItem row per episode, grouped here into one
/// poster. The key/scoring rules must stay identical to the TS helpers or
/// approve/hide lookups drift from the details modal.
/// </summary>
public sealed class ShowGroupInfo
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "Untitled";
    public string MediaType { get; set; } = "Show";
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public int? TmdbId { get; set; }
    public string? GenreIds { get; set; }
    public string? Rating { get; set; }
    public string? Networks { get; set; }
    public string? Creators { get; set; }
    public string? Cast { get; set; }
    public List<GridRow> Episodes { get; } = new();
    public List<string> EpisodeIds { get; } = new();
    public int EpisodeCount { get; set; }
    public int SeasonCount { get; set; }
    public int? LatestSeason { get; set; }
    public int? LatestEpisode { get; set; }
    public string RepresentativeId { get; set; } = "";
}

public static class ShowGrouping
{
    private static string NormalizeTitle(string? title) => (title ?? "").Trim().ToLowerInvariant();

    public static bool IsShowItem(GridRow row) =>
        !string.Equals(row.MediaType, "Movie", StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefer TMDB id, then normalized show title, then the row id.</summary>
    public static string ShowGroupKey(GridRow row)
    {
        if (row.TmdbId is > 0) return $"{row.MediaType}:tmdb:{row.TmdbId}";
        var title = NormalizeTitle(row.ShowTitle);
        return title.Length > 0
            ? $"{row.MediaType}:title:{title}"
            : $"{row.MediaType}:id:{row.Id}";
    }

    /// <summary>SxxEyy, else the normalized relative path, else the id.</summary>
    public static string EpisodeKey(GridRow row)
    {
        if (row.SeasonNumber is not null && row.EpisodeNumber is not null)
            return $"S{row.SeasonNumber}E{row.EpisodeNumber}";
        if (!string.IsNullOrWhiteSpace(row.RelativePath))
            return $"path:{row.RelativePath.Trim().ToLowerInvariant()}";
        return row.Id;
    }

    private static bool IsLaterEpisode(GridRow candidate, GridRow current)
    {
        var candidateSeason = candidate.SeasonNumber ?? -1;
        var candidateEpisode = candidate.EpisodeNumber ?? -1;
        var currentSeason = current.SeasonNumber ?? -1;
        var currentEpisode = current.EpisodeNumber ?? -1;
        return candidateSeason > currentSeason ||
               (candidateSeason == currentSeason && candidateEpisode > currentEpisode);
    }

    public static List<ShowGroupInfo> BuildGroups(IEnumerable<GridRow> rows)
    {
        var groups = new Dictionary<string, ShowGroupInfo>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!IsShowItem(row)) continue;
            var key = ShowGroupKey(row);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new ShowGroupInfo
                {
                    Key = key,
                    Title = row.ShowTitle ?? "Untitled",
                    MediaType = string.Equals(row.MediaType, "Anime", StringComparison.OrdinalIgnoreCase)
                        ? "Anime"
                        : "Show",
                    PosterUrl = row.PosterUrl,
                    Overview = row.Overview,
                    TmdbId = row.TmdbId,
                    GenreIds = row.GenreIds,
                    Rating = row.Rating,
                    Networks = row.Networks,
                    Creators = row.Creators,
                    Cast = row.Cast,
                    RepresentativeId = row.Id,
                };
                groups.Add(key, group);
            }

            group.Episodes.Add(row);
            group.EpisodeIds.Add(row.Id);
            // First non-empty wins ("" means "fetched, none found" -- keep looking).
            if (string.IsNullOrEmpty(group.PosterUrl) && !string.IsNullOrEmpty(row.PosterUrl)) group.PosterUrl = row.PosterUrl;
            if (string.IsNullOrEmpty(group.Overview) && !string.IsNullOrEmpty(row.Overview)) group.Overview = row.Overview;
            if (string.IsNullOrEmpty(group.Rating) && !string.IsNullOrEmpty(row.Rating)) group.Rating = row.Rating;
            if (string.IsNullOrEmpty(group.Networks) && !string.IsNullOrEmpty(row.Networks)) group.Networks = row.Networks;
            if (string.IsNullOrEmpty(group.Creators) && !string.IsNullOrEmpty(row.Creators)) group.Creators = row.Creators;
            if (string.IsNullOrEmpty(group.Cast) && !string.IsNullOrEmpty(row.Cast)) group.Cast = row.Cast;
        }

        foreach (var group in groups.Values)
        {
            var seasons = new HashSet<int>();
            var episodeKeys = new HashSet<string>(StringComparer.Ordinal);
            GridRow? latest = null;

            foreach (var episode in group.Episodes)
            {
                if (episode.SeasonNumber is > 0) seasons.Add(episode.SeasonNumber.Value);
                episodeKeys.Add(EpisodeKey(episode));
                if (episode.SeasonNumber is not null && episode.EpisodeNumber is not null &&
                    (latest is null || IsLaterEpisode(episode, latest)))
                {
                    latest = episode;
                }
            }

            group.EpisodeCount = episodeKeys.Count;
            group.SeasonCount = seasons.Count;
            group.LatestSeason = latest?.SeasonNumber;
            group.LatestEpisode = latest?.EpisodeNumber;
            group.RepresentativeId = group.Episodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.PosterUrl))?.Id
                                     ?? group.Episodes.FirstOrDefault()?.Id
                                     ?? group.RepresentativeId;
        }

        // Ordinal lexical order (the server contract in the research doc), with
        // key as a deterministic tie-break so paging never duplicates a card.
        // This means "10 Lives" sorts before "9" because '1' < '9' -- the same
        // lexical order a naive client sort produced, not a natural-number sort.
        return groups.Values
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>First poster-bearing episode, else the first episode.</summary>
    public static string RepresentativeId(ShowGroupInfo group) =>
        string.IsNullOrEmpty(group.RepresentativeId)
            ? group.Episodes.FirstOrDefault()?.Id ?? ""
            : group.RepresentativeId;
}
