using Curatarr.Api;
using Curatarr.Engine.Data;
using Xunit;

namespace Curatarr.Tests;

public class BootstrapAndScanScopeTests
{
    [Fact]
    public void Wizard_media_type_uses_top_level_value()
    {
        Assert.True(BootstrapWizardEndpoints.TryResolveMediaType("Show", "Movie", out var kind));
        Assert.Equal(MediaKind.Show, kind);
    }

    [Fact]
    public void Wizard_media_type_falls_back_to_legacy_library_field()
    {
        Assert.True(BootstrapWizardEndpoints.TryResolveMediaType(null, "Anime", out var kind));
        Assert.Equal(MediaKind.Anime, kind);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Movies", "Movie")]
    [InlineData("Nonsense", "Movie")]
    public void Wizard_media_type_rejects_missing_or_unknown_values(string? primary, string? legacy)
    {
        Assert.False(BootstrapWizardEndpoints.TryResolveMediaType(primary, legacy, out _));
    }

    private static SourceRoot Root(string id, MediaKind kind) => new()
    {
        Id = id,
        Name = id,
        Path = $"/media/{id}",
        MediaType = kind,
    };

    [Fact]
    public void Scoped_scan_keeps_only_matching_media_type_roots()
    {
        var roots = new[] { Root("m", MediaKind.Movie), Root("s", MediaKind.Show), Root("a", MediaKind.Anime) };

        var movies = ScanScope.FilterRoots(roots, MediaKind.Movie);
        Assert.Single(movies);
        Assert.Equal("m", movies[0].Id);

        var shows = ScanScope.FilterRoots(roots, MediaKind.Show);
        Assert.Single(shows);
        Assert.Equal("s", shows[0].Id);

        var anime = ScanScope.FilterRoots(roots, MediaKind.Anime);
        Assert.Single(anime);
        Assert.Equal("a", anime[0].Id);
    }

    [Fact]
    public void Unscoped_scan_keeps_all_roots()
    {
        var roots = new[] { Root("m", MediaKind.Movie), Root("s", MediaKind.Show), Root("a", MediaKind.Anime) };

        var all = ScanScope.FilterRoots(roots, null);
        Assert.Equal(3, all.Count);
    }
}
