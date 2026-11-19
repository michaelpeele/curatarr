namespace Curatarr.Engine.LibraryGrid;

/// <summary>
/// Pure filter predicates for the library grid. Each one mirrors the old
/// client-side behaviour in PosterGrid.tsx / showGroups.ts exactly -- so the
/// paged endpoint and the feature-flag fallback return the same cards.
/// </summary>
public static class GridFilters
{
    /// <summary>Comma-delimited TMDB genre ids, e.g. "35,10751".</summary>
    public static IReadOnlyList<string> SplitGenres(string? genreIds) =>
        string.IsNullOrEmpty(genreIds)
            ? Array.Empty<string>()
            : genreIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Pipe-delimited names (companies, networks, creators, cast).</summary>
    public static IReadOnlyList<string> SplitPipe(string? value) =>
        string.IsNullOrEmpty(value)
            ? Array.Empty<string>()
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool ContainsToken(string? haystack, string? token)
    {
        if (haystack is null || string.IsNullOrWhiteSpace(token)) return false;
        return haystack.Contains(token!.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Delimiter-aware so id 3 never matches inside 35. Null/empty never matches.
    /// </summary>
    public static bool MatchesGenre(string? genreIds, int genreId)
    {
        foreach (var part in SplitGenres(genreIds))
        {
            if (int.TryParse(part, out var id) && id == genreId) return true;
        }
        return false;
    }

    /// <summary>Unmatched means no TMDB id at all -- 0 and negative ids count too.</summary>
    public static bool IsUnmatched(int? tmdbId) => tmdbId is null or <= 0;

    public static bool IsUnmatched(GridRow row) => IsUnmatched(row.TmdbId);

    public static bool MatchesRating(string? rating, string? selected) =>
        !string.IsNullOrWhiteSpace(selected) &&
        string.Equals(rating, selected, StringComparison.Ordinal);

    public static bool MatchesStudio(string? productionCompanies, string? studio)
    {
        if (string.IsNullOrWhiteSpace(studio)) return true; // no filter
        foreach (var name in SplitPipe(productionCompanies))
        {
            if (string.Equals(name, studio, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static bool MatchesNetwork(string? networks, string? network)
    {
        if (string.IsNullOrWhiteSpace(network)) return true; // no filter
        foreach (var name in SplitPipe(networks))
        {
            if (string.Equals(name, network, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Implicit-wildcard substring tokens, AND-ed together, case-insensitive.
    /// Empty/whitespace-only tokens are ignored so a trailing space in the
    /// search box does not filter everything out.
    /// </summary>
    public static bool MatchesTokens(string? haystack, IEnumerable<string>? tokens)
    {
        if (tokens is null) return true;
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            if (!ContainsToken(haystack, token)) return false;
        }
        return true;
    }

    /// <summary>Letter-rail bucket: first A-Z character, otherwise '#'.</summary>
    public static string FirstLetter(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "#";
        var c = char.ToUpperInvariant(label.Trim()[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }

    /// <summary>
    /// 'Movie' means real movie rows (no season/episode); 'Show'/'Anime' mean
    /// episode rows of that media type. A null/blank media type means "both".
    /// </summary>
    public static bool InMediaType(GridRow row, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return true;

        if (string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(row.MediaType, "Movie", StringComparison.OrdinalIgnoreCase)
                   && row.SeasonNumber is null
                   && row.EpisodeNumber is null;
        }

        if (!string.Equals(row.MediaType, mediaType, StringComparison.OrdinalIgnoreCase)) return false;
        return row.SeasonNumber is not null || row.EpisodeNumber is not null;
    }
}
