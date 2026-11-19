using Curatarr.Engine.LibraryGrid;

namespace Curatarr.Tests;

internal static class TestRows
{
    public static GridRow Movie(string id, string title) => new()
    {
        Id = id,
        MediaType = "Movie",
        Title = title,
    };

    public static GridRow Episode(
        string id,
        string? showTitle,
        int season,
        int episode,
        int tmdbId = 0,
        string mediaType = "Show",
        string? relativePath = null) => new()
    {
        Id = id,
        MediaType = mediaType,
        ShowTitle = showTitle,
        SeasonNumber = season,
        EpisodeNumber = episode,
        TmdbId = tmdbId,
        RelativePath = relativePath,
    };

    public static AnnotationMap Annotations(
        (string Id, DateTime At)[]? approved = null,
        string[]? hidden = null,
        (string Id, DateTime At)[]? hiddenAt = null) => new(
        (approved ?? []).ToDictionary(a => a.Id, a => a.At),
        (hidden ?? []).ToHashSet(),
        (hiddenAt ?? []).ToDictionary(a => a.Id, a => a.At));
}
