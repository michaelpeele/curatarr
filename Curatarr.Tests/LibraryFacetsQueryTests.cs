using Curatarr.Engine.LibraryGrid;
using Xunit;

namespace Curatarr.Tests;

public class LibraryFacetsQueryTests
{
    [Fact]
    public void Ratings_are_deduplicated_and_use_the_canonical_order()
    {
        var rows = new[]
        {
            new GridRow { Id = "m1", MediaType = "Movie", Title = "A", Rating = "R" },
            new GridRow { Id = "m2", MediaType = "Movie", Title = "B", Rating = "PG" },
            new GridRow { Id = "m3", MediaType = "Movie", Title = "C", Rating = "PG" },
            new GridRow { Id = "m4", MediaType = "Movie", Title = "D", Rating = "Weird" },
        };

        var facets = LibraryFacetsQuery.Build(rows, "Movie");

        Assert.Equal(new[] { "PG", "R", "Weird" }, facets.Ratings);
    }

    [Fact]
    public void Tv_ratings_are_used_for_show_libraries()
    {
        var rows = new[]
        {
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 1),
            TestRows.Episode("e2", "Show", 1, 2, tmdbId: 1),
        };
        rows[0].Rating = "TV-MA";
        rows[1].Rating = "TV-PG";

        var facets = LibraryFacetsQuery.Build(rows, "Show");

        Assert.Equal(new[] { "TV-PG", "TV-MA" }, facets.Ratings);
    }

    [Fact]
    public void Studios_are_split_on_pipe_and_deduplicated()
    {
        var rows = new[]
        {
            new GridRow { Id = "m1", MediaType = "Movie", Title = "A", ProductionCompanies = "Pixar|Pixar" },
            new GridRow { Id = "m2", MediaType = "Movie", Title = "B", ProductionCompanies = "Disney|Pixar" },
        };

        var facets = LibraryFacetsQuery.Build(rows, "Movie");

        Assert.Equal(new[] { "Disney", "Pixar" }, facets.Studios);
    }

    [Fact]
    public void Networks_are_only_emitted_for_non_movie_media_types()
    {
        var show = TestRows.Episode("e1", "Show", 1, 1, tmdbId: 1);
        show.Networks = "PBS|NBC";
        var movie = new GridRow
        {
            Id = "m1", MediaType = "Movie", Title = "Movie", ProductionCompanies = "Pixar",
            Networks = "ShouldNotAppear",
        };

        Assert.Equal(new[] { "NBC", "PBS" }, LibraryFacetsQuery.Build(new[] { show, movie }, "Show").Networks);
        Assert.Empty(LibraryFacetsQuery.Build(new[] { show, movie }, "Movie").Networks);
    }

    [Fact]
    public void Facets_honour_media_type_and_ignore_paging_concepts()
    {
        var rows = new[]
        {
            new GridRow { Id = "m1", MediaType = "Movie", Title = "A", Rating = "PG", GenreIds = "35,10751" },
            new GridRow { Id = "m2", MediaType = "Movie", Title = "B", Rating = "PG-13", GenreIds = "35" },
            TestRows.Episode("e1", "Show", 1, 1, tmdbId: 1),
        };
        rows[2].Rating = "TV-14";

        var all = LibraryFacetsQuery.Build(rows, null);
        var movies = LibraryFacetsQuery.Build(rows, "Movie");

        Assert.Equal(new[] { "PG", "PG-13", "TV-14" }, all.Ratings);
        Assert.Equal(new[] { "PG", "PG-13" }, movies.Ratings);
        // Genre counts come from splitting the CSV, one count per row.
        Assert.Equal(new[] { 35, 10751 }, movies.Genres.Select(g => g.Id));
        Assert.Equal(2, movies.Genres.Single(g => g.Id == 35).Count);
        Assert.Equal(1, movies.Genres.Single(g => g.Id == 10751).Count);
        // No q/letter/offset/limit parameters exist on the facet query at all.
        Assert.Equal(all.Ratings, LibraryFacetsQuery.Build(rows, null).Ratings);
    }

    [Fact]
    public void Hidden_rows_are_excluded_unless_the_hidden_view_is_requested()
    {
        var rows = new[]
        {
            new GridRow { Id = "m1", MediaType = "Movie", Title = "A", Rating = "PG" },
            new GridRow { Id = "m2", MediaType = "Movie", Title = "B", Rating = "R" },
        };
        var annotations = TestRows.Annotations(hidden: new[] { "m2" });

        Assert.Equal(new[] { "PG" }, LibraryFacetsQuery.Build(rows, "Movie", annotations).Ratings);
        Assert.Equal(new[] { "R" }, LibraryFacetsQuery.Build(rows, "Movie", annotations, "hidden").Ratings);
    }
}
