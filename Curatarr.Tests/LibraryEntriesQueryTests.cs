using Curatarr.Engine.LibraryGrid;
using Xunit;

namespace Curatarr.Tests;

public class LibraryEntriesQueryTests
{
    private static List<GridRow> Movies(int count)
    {
        var rows = new List<GridRow>();
        for (var i = 1; i <= count; i++)
        {
            rows.Add(new GridRow
            {
                Id = $"m{i:000}",
                MediaType = "Movie",
                Title = $"Title {i:000}",
            });
        }
        return rows;
    }

    private static LibraryEntriesPageDto Build(
        IReadOnlyList<GridRow> rows,
        LibraryGridQuerySpec spec,
        AnnotationMap? annotations = null) =>
        LibraryEntriesQuery.Build(rows, spec, annotations);

    [Fact]
    public void Limit_defaults_to_60_and_clamps_to_1_through_200()
    {
        Assert.Equal(GridPaging.DefaultLimit, GridPaging.ClampLimit(null));
        Assert.Equal(1, GridPaging.ClampLimit(0));
        Assert.Equal(1, GridPaging.ClampLimit(-5));
        Assert.Equal(200, GridPaging.ClampLimit(500));
        Assert.Equal(60, GridPaging.ClampLimit(60));

        var page = Build(Movies(3), new LibraryGridQuerySpec { Limit = 500 });
        Assert.Equal(200, page.Limit);
    }

    [Fact]
    public void Negative_offset_is_clamped_to_zero()
    {
        Assert.Equal(0, GridPaging.NormalizeOffset(null));
        Assert.Equal(0, GridPaging.NormalizeOffset(-10));
        Assert.Equal(7, GridPaging.NormalizeOffset(7));

        var page = Build(Movies(3), new LibraryGridQuerySpec { Offset = -10 });
        Assert.Equal(0, page.Offset);
        Assert.Equal(3, page.Entries.Count);
    }

    [Fact]
    public void Total_is_filtered_count_and_has_more_uses_returned_page_size()
    {
        var page = Build(Movies(150), new LibraryGridQuerySpec { Offset = 0, Limit = 60 });

        Assert.Equal(150, page.Total);
        Assert.Equal(60, page.Entries.Count);
        Assert.True(page.HasMore);

        var filtered = Build(Movies(150), new LibraryGridQuerySpec { Q = "Title 001", Limit = 60 });
        Assert.Equal(1, filtered.Total);
        Assert.False(filtered.HasMore);
    }

    [Fact]
    public void Page_two_returns_the_next_sixty_in_the_same_order()
    {
        var first = Build(Movies(150), new LibraryGridQuerySpec { Offset = 0, Limit = 60 });
        var second = Build(Movies(150), new LibraryGridQuerySpec { Offset = 60, Limit = 60 });

        Assert.Equal("m001", first.Entries[0].Id);
        Assert.Equal("m060", first.Entries[^1].Id);
        Assert.Equal("m061", second.Entries[0].Id);
        Assert.Equal("m120", second.Entries[^1].Id);
        Assert.Empty(first.Entries.Select(e => e.Id).Intersect(second.Entries.Select(e => e.Id)));
    }

    [Fact]
    public void Last_page_is_short_and_reports_has_more_false()
    {
        var page = Build(Movies(150), new LibraryGridQuerySpec { Offset = 120, Limit = 60 });

        Assert.Equal(30, page.Entries.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Offset_past_total_returns_empty_page_not_404()
    {
        var page = Build(Movies(3), new LibraryGridQuerySpec { Offset = 99, Limit = 60 });

        Assert.Equal(3, page.Total);
        Assert.Empty(page.Entries);
        Assert.False(page.HasMore);
        Assert.Equal(99, page.Offset);
    }

    [Fact]
    public void Tied_titles_are_broken_by_id_so_pages_never_duplicate()
    {
        var rows = Enumerable.Range(1, 5)
            .Select(i => new GridRow { Id = $"id{i}", MediaType = "Movie", Title = "Same Title" })
            .ToList();
        var first = Build(rows, new LibraryGridQuerySpec { Limit = 2 });
        var second = Build(rows, new LibraryGridQuerySpec { Offset = 2, Limit = 2 });
        var third = Build(rows, new LibraryGridQuerySpec { Offset = 4, Limit = 2 });

        Assert.Equal(new[] { "id1", "id2" }, first.Entries.Select(e => e.Id));
        Assert.Equal(new[] { "id3", "id4" }, second.Entries.Select(e => e.Id));
        Assert.Equal(new[] { "id5" }, third.Entries.Select(e => e.Id));
    }

    [Fact]
    public void No_library_id_means_all_false_annotations()
    {
        var page = Build(new[] { TestRows.Movie("m1", "Movie") }, new LibraryGridQuerySpec());

        var entry = Assert.Single(page.Entries);
        Assert.False(entry.Approved);
        Assert.Null(entry.ApprovedAt);
        Assert.False(entry.Hidden);
        Assert.Null(entry.HiddenAt);
    }

    [Fact]
    public void Movie_approval_uses_the_exact_timestamp()
    {
        var stamp = new DateTime(2026, 11, 17, 12, 34, 56, DateTimeKind.Utc);
        var annotations = TestRows.Annotations(new[] { ("m1", stamp) });
        var page = Build(new[] { TestRows.Movie("m1", "Movie") }, new LibraryGridQuerySpec(), annotations);

        var entry = Assert.Single(page.Entries);
        Assert.True(entry.Approved);
        Assert.Equal(stamp, entry.ApprovedAt);
    }

    [Fact]
    public void Show_group_is_approved_when_any_episode_is_with_the_newest_stamp()
    {
        var older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 10),
            TestRows.Episode("e2", "Show", 1, 2, tmdbId: 10),
        };
        var annotations = TestRows.Annotations(new[] { ("e1", older), ("e2", newer) });

        var page = Build(rows, new LibraryGridQuerySpec { MediaType = "Show" }, annotations);
        var entry = Assert.Single(page.Entries);

        Assert.Equal("show", entry.Kind);
        Assert.True(entry.Approved);
        Assert.Equal(newer, entry.ApprovedAt);
        Assert.Equal(new[] { "e1", "e2" }, entry.EpisodeIds);
    }

    [Fact]
    public void Show_group_is_hidden_only_when_every_episode_is_hidden()
    {
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 11),
            TestRows.Episode("e2", "Show", 1, 2, tmdbId: 11),
        };

        var partial = Build(rows, new LibraryGridQuerySpec { MediaType = "Show" },
            TestRows.Annotations(hidden: new[] { "e1" }));
        Assert.False(Assert.Single(partial.Entries).Hidden);

        var all = Build(rows, new LibraryGridQuerySpec { MediaType = "Show", View = "hidden" },
            TestRows.Annotations(hidden: new[] { "e1", "e2" }));
        Assert.True(Assert.Single(all.Entries).Hidden);
    }

    [Fact]
    public void Hidden_entries_only_appear_in_the_hidden_view()
    {
        var rows = new[]
        {
            TestRows.Movie("m1", "Visible"),
            TestRows.Movie("m2", "Hidden"),
        };
        var annotations = TestRows.Annotations(
            approved: new[] { ("m1", DateTime.UtcNow) },
            hidden: new[] { "m2" });

        // Hidden m2 is excluded from every non-hidden view. m1 is approved,
        // so it shows in all/library but not in unmarked.
        foreach (var view in new[] { "all", "library" })
        {
            var page = Build(rows, new LibraryGridQuerySpec { View = view }, annotations);
            Assert.Equal(1, page.Total);
            Assert.Equal("m1", Assert.Single(page.Entries).Id);
        }
        Assert.Equal(0, Build(rows, new LibraryGridQuerySpec { View = "unmarked" }, annotations).Total);

        var hidden = Build(rows, new LibraryGridQuerySpec { View = "hidden" }, annotations);
        Assert.Equal(1, hidden.Total);
        Assert.Equal("m2", Assert.Single(hidden.Entries).Id);
    }

    [Fact]
    public void Library_and_unmarked_views_partition_approved_entries()
    {
        var rows = new[] { TestRows.Movie("m1", "A"), TestRows.Movie("m2", "B") };
        var annotations = TestRows.Annotations(new[] { ("m1", DateTime.UtcNow) });

        var library = Build(rows, new LibraryGridQuerySpec { View = "library" }, annotations);
        var unmarked = Build(rows, new LibraryGridQuerySpec { View = "unmarked" }, annotations);

        Assert.Equal("m1", Assert.Single(library.Entries).Id);
        Assert.Equal("m2", Assert.Single(unmarked.Entries).Id);
    }

    [Fact]
    public void Show_groups_have_the_documented_group_metadata()
    {
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 12),
            TestRows.Episode("e2", "Show", 2, 4, tmdbId: 12),
        };
        var page = Build(rows, new LibraryGridQuerySpec { MediaType = "Show" });
        var entry = Assert.Single(page.Entries);

        Assert.Equal("Show:tmdb:12", entry.Key);
        Assert.Equal(2, entry.EpisodeCount);
        Assert.Equal(2, entry.SeasonCount);
        Assert.Equal(2, entry.LatestSeason);
        Assert.Equal(4, entry.LatestEpisode);
        Assert.NotNull(entry.RepresentativeId);
    }

    [Fact]
    public void Episode_ids_are_capped_for_the_details_modal()
    {
        var rows = Enumerable.Range(1, 250)
            .Select(i => TestRows.Episode($"e{i}", "Big Show", 1, i, tmdbId: 99))
            .ToList();

        var page = Build(rows, new LibraryGridQuerySpec { MediaType = "Show" });
        var entry = Assert.Single(page.Entries);

        Assert.Equal(LibraryEntriesQuery.EpisodeIdLimit, entry.EpisodeIds!.Length);
        Assert.Equal(250, entry.EpisodeCount);
    }

    [Fact]
    public void HiddenAt_is_null_when_nothing_is_hidden()
    {
        var page = Build(new[] { TestRows.Movie("m1", "Visible") }, new LibraryGridQuerySpec());
        Assert.Null(Assert.Single(page.Entries).HiddenAt);

        var annotated = Build(new[] { TestRows.Movie("m2", "Also visible") },
            new LibraryGridQuerySpec(),
            TestRows.Annotations(approved: new[] { ("m2", DateTime.UtcNow) }));
        Assert.Null(Assert.Single(annotated.Entries).HiddenAt);
    }

    [Fact]
    public void Movie_hiddenAt_is_the_exact_hide_timestamp()
    {
        var stamp = new DateTime(2026, 11, 17, 9, 5, 0, DateTimeKind.Utc);
        var annotations = TestRows.Annotations(
            hidden: new[] { "m1" },
            hiddenAt: new[] { ("m1", stamp) });

        var page = Build(new[] { TestRows.Movie("m1", "Hidden") },
            new LibraryGridQuerySpec { View = "hidden" }, annotations);

        var entry = Assert.Single(page.Entries);
        Assert.True(entry.Hidden);
        Assert.Equal(stamp, entry.HiddenAt);
    }

    [Fact]
    public void Show_group_hiddenAt_is_the_newest_stamp_among_episode_ids()
    {
        var older = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 20),
            TestRows.Episode("e2", "Show", 1, 2, tmdbId: 20),
        };
        var annotations = TestRows.Annotations(
            hidden: new[] { "e1", "e2" },
            hiddenAt: new[] { ("e1", older), ("e2", newer) });

        var page = Build(rows, new LibraryGridQuerySpec { MediaType = "Show", View = "hidden" }, annotations);

        var entry = Assert.Single(page.Entries);
        Assert.True(entry.Hidden);
        Assert.Equal(newer, entry.HiddenAt);
    }

    [Fact]
    public void Show_hiddenAt_is_reported_even_when_not_all_episodes_are_hidden()
    {
        var stamp = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 21),
            TestRows.Episode("e2", "Show", 1, 2, tmdbId: 21),
        };
        var annotations = TestRows.Annotations(
            hidden: new[] { "e1" },
            hiddenAt: new[] { ("e1", stamp) });

        var page = Build(rows, new LibraryGridQuerySpec { MediaType = "Show" }, annotations);

        var entry = Assert.Single(page.Entries);
        Assert.False(entry.Hidden);
        Assert.Equal(stamp, entry.HiddenAt);
    }

    [Fact]
    public void NewestHiddenAt_only_considers_supplied_ids()
    {
        var known = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var annotations = TestRows.Annotations(
            hidden: new[] { "m1", "m2" },
            hiddenAt: new[] { ("m1", known), ("m2", known.AddDays(1)) });

        Assert.Equal(known, annotations.NewestHiddenAt(new[] { "m1" }));
        Assert.Equal(known.AddDays(1), annotations.NewestHiddenAt(new[] { "m1", "m2" }));
        Assert.Null(annotations.NewestHiddenAt(new[] { "missing" }));
        Assert.Null(annotations.NewestHiddenAt(Array.Empty<string>()));
        Assert.Null(AnnotationMap.Empty.NewestHiddenAt(new[] { "m1" }));
    }
}
