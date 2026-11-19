using Curatarr.Engine.LibraryGrid;
using Xunit;

namespace Curatarr.Tests;

public class ShowGroupingTests
{
    [Fact]
    public void Same_tmdb_id_same_media_type_is_one_group()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "Show A", 1, 1, tmdbId: 123),
            TestRows.Episode("e2", "Show A", 1, 2, tmdbId: 123),
        });

        var group = Assert.Single(groups);
        Assert.Equal("Show:tmdb:123", group.Key);
        Assert.Equal(2, group.EpisodeIds.Count);
    }

    [Fact]
    public void Missing_tmdb_id_falls_back_to_lowercased_show_title()
    {
        var groups = ShowGrouping.BuildGroups(new[] { TestRows.Episode("e1", "  The Show  ", 1, 1) });

        Assert.Equal("Show:title:the show", groups[0].Key);
    }

    [Fact]
    public void Missing_tmdb_id_and_blank_title_use_the_row_id()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "", 1, 1),
            TestRows.Episode("e2", null, 1, 1),
        });

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Key == "Show:id:e1");
        Assert.Contains(groups, g => g.Key == "Show:id:e2");
    }

    [Fact]
    public void Same_tmdb_id_different_media_type_makes_two_groups()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "Same", 1, 1, tmdbId: 7, mediaType: "Show"),
            TestRows.Episode("e2", "Same", 1, 1, tmdbId: 7, mediaType: "Anime"),
        });

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Key == "Show:tmdb:7");
        Assert.Contains(groups, g => g.Key == "Anime:tmdb:7");
    }

    [Fact]
    public void Display_fields_come_from_first_non_empty_episode()
    {
        var first = TestRows.Episode("e1", "Show", 1, 1);
        var second = TestRows.Episode("e2", "Show", 1, 2);
        second.PosterUrl = "poster";
        second.Overview = "overview";
        second.Rating = "TV-14";
        second.Networks = "PBS|NBC";
        second.Creators = "Creator";
        second.Cast = "Actor";

        var group = Assert.Single(ShowGrouping.BuildGroups(new[] { first, second }));

        Assert.Equal("poster", group.PosterUrl);
        Assert.Equal("overview", group.Overview);
        Assert.Equal("TV-14", group.Rating);
        Assert.Equal("PBS|NBC", group.Networks);
        Assert.Equal("Creator", group.Creators);
        Assert.Equal("Actor", group.Cast);
        Assert.Equal("e2", group.RepresentativeId);
    }

    [Fact]
    public void Episode_count_deduplicates_by_season_episode()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 1),
            TestRows.Episode("e2", "Show", 1, 1, tmdbId: 1),
            TestRows.Episode("e3", "Show", 1, 2, tmdbId: 1),
        });

        Assert.Equal(2, groups[0].EpisodeCount);
    }

    [Fact]
    public void Episode_count_falls_back_to_normalized_relative_path()
    {
        var a = TestRows.Episode("e1", "Loose", 0, 0, tmdbId: 2, relativePath: "  Show/Ep1.mkv  ");
        var b = TestRows.Episode("e2", "Loose", 0, 0, tmdbId: 2, relativePath: "show/ep1.mkv");
        a.SeasonNumber = null; a.EpisodeNumber = null;
        b.SeasonNumber = null; b.EpisodeNumber = null;

        var group = Assert.Single(ShowGrouping.BuildGroups(new[] { a, b }));

        Assert.Equal(1, group.EpisodeCount);
        Assert.Equal("path:show/ep1.mkv", ShowGrouping.EpisodeKey(a));
    }

    [Fact]
    public void Season_count_ignores_zero_and_null_seasons()
    {
        var a = TestRows.Episode("e1", "Show", 0, 1, tmdbId: 3);
        var b = TestRows.Episode("e2", "Show", 2, 1, tmdbId: 3);
        var c = TestRows.Episode("e3", "Show", 2, 2, tmdbId: 3);
        c.SeasonNumber = null;

        var group = Assert.Single(ShowGrouping.BuildGroups(new[] { a, b, c }));

        Assert.Equal(1, group.SeasonCount);
    }

    [Fact]
    public void Latest_season_and_episode_pick_highest_pair()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "Show", 3, 2, tmdbId: 4),
            TestRows.Episode("e2", "Show", 2, 11, tmdbId: 4),
            TestRows.Episode("e3", "Show", 3, 9, tmdbId: 4),
        });

        Assert.Equal(3, groups[0].LatestSeason);
        Assert.Equal(9, groups[0].LatestEpisode);
    }

    [Fact]
    public void Representative_is_first_poster_bearing_episode_then_first()
    {
        var first = TestRows.Episode("e1", "Show", 1, 1, tmdbId: 5);
        var second = TestRows.Episode("e2", "Show", 1, 2, tmdbId: 5);
        second.PosterUrl = "p";

        var group = Assert.Single(ShowGrouping.BuildGroups(new[] { first, second }));
        Assert.Equal("e2", group.RepresentativeId);

        var noPoster = Assert.Single(ShowGrouping.BuildGroups(new[] { first }));
        Assert.Equal("e1", noPoster.RepresentativeId);
    }

    [Fact]
    public void Sorting_is_ordinal_case_insensitive_documented_digit_order()
    {
        var groups = ShowGrouping.BuildGroups(new[]
        {
            TestRows.Episode("e1", "9", 1, 1, tmdbId: 1),
            TestRows.Episode("e2", "10 Lives", 1, 1, tmdbId: 2),
            TestRows.Episode("e3", "a bug's life", 1, 1, tmdbId: 3),
        });

        // '1' < '9' < 'a' under ordinal lexical ordering; this is the chosen
        // server contract (not a natural-number sort).
        Assert.Equal(new[] { "10 Lives", "9", "a bug's life" }, groups.Select(g => g.Title));
    }
}
