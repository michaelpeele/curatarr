using Curatarr.Engine.Data;

namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// The flat projection a library-grid request needs from <see cref="SourceItem"/>.
///
/// This deliberately carries no EF plumbing and no navigation properties: the
/// pure helpers in this folder (and their unit tests) can build rows by hand,
/// and the API endpoint projects straight into it. Keeping the projection to
/// the 18 fields used by the grid is what makes a paged response small -- the
/// client no longer downloads 82k rows just to render 60 cards.
/// </summary>
public sealed class GridRow
{
    public string Id { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? TmdbId { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public string? RelativePath { get; set; }
    public string? GenreIds { get; set; }
    public string? Rating { get; set; }
    public string? ProductionCompanies { get; set; }
    public string? Networks { get; set; }
    public string? Director { get; set; }
    public string? Creators { get; set; }
    public string? Cast { get; set; }

    public static GridRow FromSourceItem(SourceItem item) => new()
    {
        Id = item.Id,
        MediaType = item.MediaType.ToString(),
        Title = item.Title,
        Year = item.Year,
        ShowTitle = item.ShowTitle,
        SeasonNumber = item.SeasonNumber,
        EpisodeNumber = item.EpisodeNumber,
        TmdbId = item.TmdbId,
        PosterUrl = item.PosterUrl,
        Overview = item.Overview,
        RelativePath = item.RelativePath,
        GenreIds = item.GenreIds,
        Rating = item.Rating,
        ProductionCompanies = item.ProductionCompanies,
        Networks = item.Networks,
        Director = item.Director,
        Creators = item.Creators,
        Cast = item.Cast,
    };
}
