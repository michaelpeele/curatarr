using Curatarr.Engine.LibraryGrid;
using Xunit;

namespace Curatarr.Tests;

public class GridFilterTests
{
    [Fact]
    public void Genre_matches_whole_ids_only()
    {
        Assert.True(GridFilters.MatchesGenre("35,10751", 35));
        Assert.False(GridFilters.MatchesGenre("35,10751", 3));
        Assert.False(GridFilters.MatchesGenre("35,10751", 5));
    }

    [Fact]
    public void Genre_null_or_empty_never_matches()
    {
        Assert.False(GridFilters.MatchesGenre(null, 35));
        Assert.False(GridFilters.MatchesGenre("", 35));
    }

    [Fact]
    public void Studio_is_delimiter_exact_not_a_prefix_match()
    {
        Assert.True(GridFilters.MatchesStudio("Pixar|Walt Disney Pictures", "Pixar"));
        Assert.False(GridFilters.MatchesStudio("Pixar|Walt Disney Pictures", "Pi"));
        Assert.False(GridFilters.MatchesStudio(null, "Pixar"));
    }

    [Fact]
    public void Network_is_delimiter_exact_and_null_safe()
    {
        Assert.False(GridFilters.MatchesNetwork(null, "NBC"));
        Assert.True(GridFilters.MatchesNetwork("PBS|NBC", "NBC"));
        Assert.False(GridFilters.MatchesNetwork("PBS|NBC", "NB"));
    }

    [Fact]
    public void Rating_is_an_exact_string_match()
    {
        Assert.True(GridFilters.MatchesRating("PG", "PG"));
        Assert.False(GridFilters.MatchesRating("PG-13", "PG"));
        Assert.False(GridFilters.MatchesRating(null, "PG"));
    }

    [Fact]
    public void Movie_rows_only_match_movie_media_type()
    {
        Assert.True(GridFilters.InMediaType(TestRows.Movie("m", "Movie"), "Movie"));
        var episode = TestRows.Episode("e", "Show", 1, 1);
        Assert.False(GridFilters.InMediaType(episode, "Movie"));
    }

    [Fact]
    public void Episode_rows_only_match_their_own_show_media_type()
    {
        Assert.True(GridFilters.InMediaType(TestRows.Episode("e", "Show", 1, 1), "Show"));
        Assert.False(GridFilters.InMediaType(TestRows.Movie("m", "Movie"), "Show"));
        Assert.True(GridFilters.InMediaType(
            TestRows.Episode("a", "Anime", 1, 1, mediaType: "Anime"), "Anime"));
    }

    [Fact]
    public void Tokens_are_anded()
    {
        Assert.True(GridFilters.MatchesTokens("Cinderella", new[] { "cind" }));
        Assert.False(GridFilters.MatchesTokens("Cinderella", new[] { "cind", "zzz" }));
    }

    [Fact]
    public void Tokens_are_case_insensitive_and_ignore_empties()
    {
        Assert.True(GridFilters.MatchesTokens("CINDERELLA", new[] { "  cind  " }));
        Assert.True(GridFilters.MatchesTokens("Cinderella", new[] { "", "   ", "rell" }));
        Assert.True(GridFilters.MatchesTokens(null, new[] { " " }));
    }

    [Fact]
    public void First_letter_buckets_non_letters_as_hash()
    {
        Assert.Equal("#", GridFilters.FirstLetter("10 Lives"));
        Assert.Equal("A", GridFilters.FirstLetter("a bug's life"));
        Assert.Equal("#", GridFilters.FirstLetter(""));
        Assert.Equal("#", GridFilters.FirstLetter(null));
    }

    [Fact]
    public void Split_pipe_trims_and_drops_empty_names()
    {
        Assert.Equal(new[] { "Pixar", "Disney" }, GridFilters.SplitPipe(" Pixar || Disney |"));
        Assert.Empty(GridFilters.SplitPipe(null));
    }
}
