using Curatarr.Engine.Data;
using Curatarr.Engine.LibraryGrid;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Curatarr.Tests;

/// <summary>
/// Guards the EF shape used by LibraryGridEndpoints: it must translate on the
/// same SQLite provider prod uses (enum string converter + conditional media
/// predicate + delimiter prefilters + 18-column projection).
/// </summary>
public class EndpointQueryTranslationTests
{
    [Fact]
    public async Task Endpoint_style_query_translates_and_feeds_the_pure_query()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CuratarrDbContext>().UseSqlite(connection).Options;
        using var db = new CuratarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.SourceRoots.Add(new SourceRoot { Id = "root", Name = "Root", Path = "/media", MediaType = MediaKind.Movie });
        db.SourceItems.Add(new SourceItem
        {
            Id = "m1",
            SourceRootId = "root",
            RelativePath = "movie.mkv",
            MediaType = MediaKind.Movie,
            Title = "Movie",
            GenreIds = "35,10751",
            Rating = "PG",
            ProductionCompanies = "Pixar|Disney",
        });
        await db.SaveChangesAsync();

        MediaKind? kind = MediaKind.Movie;
        var isMovie = true;
        var rating = "PG";
        var studio = "Pixar";

        var query = db.SourceItems.AsNoTracking()
            .Where(i => i.MediaType == kind.Value &&
                (isMovie
                    ? i.SeasonNumber == null && i.EpisodeNumber == null
                    : i.SeasonNumber != null || i.EpisodeNumber != null))
            .Where(i => i.Rating == rating)
            .Where(i => i.ProductionCompanies != null && i.ProductionCompanies.Contains(studio));

        var raw = await query.Select(i => new
        {
            i.Id, i.MediaType, i.Title, i.Year,
            i.ShowTitle, i.SeasonNumber, i.EpisodeNumber,
            i.TmdbId, i.PosterUrl, i.Overview, i.RelativePath,
            i.GenreIds, i.Rating, i.ProductionCompanies, i.Networks,
            i.Director, i.Creators, i.Cast
        }).ToListAsync();

        var rows = raw.Select(i => new GridRow
        {
            Id = i.Id,
            MediaType = i.MediaType.ToString(),
            Title = i.Title,
            Year = i.Year,
            ShowTitle = i.ShowTitle,
            SeasonNumber = i.SeasonNumber,
            EpisodeNumber = i.EpisodeNumber,
            TmdbId = i.TmdbId,
            PosterUrl = i.PosterUrl,
            Overview = i.Overview,
            RelativePath = i.RelativePath,
            GenreIds = i.GenreIds,
            Rating = i.Rating,
            ProductionCompanies = i.ProductionCompanies,
            Networks = i.Networks,
            Director = i.Director,
            Creators = i.Creators,
            Cast = i.Cast,
        }).ToList();

        var page = LibraryEntriesQuery.Build(rows, new LibraryGridQuerySpec
        {
            MediaType = "Movie",
            Rating = "PG",
            Studio = "Pixar",
            Genres = new[] { 35 },
        });

        Assert.Single(raw);
        Assert.Equal("Movie", rows[0].MediaType);
        Assert.Equal("m1", Assert.Single(page.Entries).Id);
    }
}
