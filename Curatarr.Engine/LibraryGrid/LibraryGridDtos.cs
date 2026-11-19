using System.Text.Json.Serialization;

namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// One card in a paged library response. Movies use Id, shows use Key; the
/// show-only fields are omitted from JSON so a page of movies stays small.
/// </summary>
public sealed class LibraryEntryDto
{
    public string Kind { get; set; } = "movie";
    public string? Id { get; set; }
    public string? Key { get; set; }
    public string Title { get; set; } = "";
    public string MediaType { get; set; } = "Movie";

    public string? PosterUrl { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Overview { get; set; }
    public int? Year { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShowTitle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SeasonNumber { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EpisodeNumber { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TmdbId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelativePath { get; set; }

    public int[]? GenreIds { get; set; }
    public string? Rating { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductionCompanies { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Networks { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Director { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Creators { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cast { get; set; }

    public bool Approved { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public bool Hidden { get; set; }
    /// <summary>
    /// When the hide was recorded (newest episode stamp for show groups). The
    /// card eye uses it with lastReconciledAt to show pending (yellow) vs
    /// confirmed (green), mirroring ApprovedAt on the check icon (S5 #9).
    /// </summary>
    public DateTime? HiddenAt { get; set; }

    // Show-only metadata
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SeasonCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EpisodeCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LatestSeason { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LatestEpisode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RepresentativeId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? EpisodeIds { get; set; }
}

public sealed class LibraryEntriesPageDto
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public bool HasMore { get; set; }
    public string? MediaType { get; set; }
    public IReadOnlyList<LibraryEntryDto> Entries { get; set; } = Array.Empty<LibraryEntryDto>();
}

public sealed class LibraryFacetsDto
{
    public IReadOnlyList<string> Ratings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Studios { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Networks { get; set; } = Array.Empty<string>();
    public IReadOnlyList<GenreFacetDto> Genres { get; set; } = Array.Empty<GenreFacetDto>();
}

public sealed class GenreFacetDto
{
    public int Id { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Read-only annotations for one library. No libraryId means empty maps, so
/// every entry reports approved=false / approvedAt=null / hidden=false /
/// hiddenAt=null.
/// </summary>
public sealed class AnnotationMap
{
    public static AnnotationMap Empty { get; } = new(
        new Dictionary<string, DateTime>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    public AnnotationMap(
        IReadOnlyDictionary<string, DateTime> approvedAtByItem,
        IReadOnlySet<string> hiddenIds,
        IReadOnlyDictionary<string, DateTime>? hiddenAtByItem = null)
    {
        ApprovedAtByItem = approvedAtByItem;
        HiddenIds = hiddenIds;
        HiddenAtByItem = hiddenAtByItem ?? new Dictionary<string, DateTime>(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, DateTime> ApprovedAtByItem { get; }
    public IReadOnlySet<string> HiddenIds { get; }
    public IReadOnlyDictionary<string, DateTime> HiddenAtByItem { get; }

    /// <summary>Newest hide stamp among the supplied ids; null when none is hidden.</summary>
    public DateTime? NewestHiddenAt(IEnumerable<string> ids)
    {
        DateTime? newest = null;
        foreach (var id in ids)
        {
            if (HiddenAtByItem.TryGetValue(id, out var at) && (newest is null || at > newest.Value))
                newest = at;
        }
        return newest;
    }
}

public sealed class LibraryGridQuerySpec
{
    public string? MediaType { get; init; }
    public string? Q { get; init; }
    public IReadOnlyList<int> Genres { get; init; } = Array.Empty<int>();
    public string? Rating { get; init; }
    public string? Studio { get; init; }
    public string? Network { get; init; }
    public string? Letter { get; init; }
    public bool IsUnmatched { get; init; }
    public string View { get; init; } = "all";
    public int? Offset { get; init; }
    public int? Limit { get; init; }
}
