using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Curatarr.Engine.Data;

namespace Curatarr.Engine;

public record MovieMetadata(int TmdbId, string Title, int? Year, string? PosterUrl, string? Overview, List<int>? GenreIds = null);
public record ShowMetadata(int TmdbId, string Title, string? PosterUrl, string? Overview, List<int>? GenreIds = null);

/// <summary>One candidate returned by the admin Match tooling.</summary>
public sealed record TitleSearchResult(string MediaType, int TmdbId, string Title, int? Year, string? PosterUrl, string? Overview);

/// <summary>
/// Everything the catalog filter UI needs beyond scan-time metadata:
/// genre ids plus the US certification and studio/network names, fetched
/// in a single TMDB round-trip per title (details + appended ratings).
/// Director (movies) / Creators (shows) and the top cast are included so
/// the free-text search can match people names.
/// </summary>
public record CatalogDetails(
    List<int> GenreIds,
    string? Rating,                    // US certification; null = TMDB has none
    List<string>? ProductionCompanies, // movies only
    List<string>? Networks,            // shows/anime only
    string? Director,                  // movies only
    List<string>? Creators,            // shows/anime only
    List<string>? Cast,                // both, top ~30 in billing order
    string RawJson,                    // full raw response, stashed in TmdbRawDetails
    string RequestedWith               // append_to_response set used for this fetch
);

public record TitleDetails(
    int TmdbId,
    string Title,
    string? Overview,
    string? PosterUrl,
    string? Director,
    List<string>? Creators,
    List<string> Cast,
    List<int>? GenreIds = null
);

public interface IMetadataProvider
{
    Task<MovieMetadata?> SearchMovieAsync(string title, int? year, CancellationToken ct = default);
    Task<ShowMetadata?> SearchShowAsync(string title, CancellationToken ct = default);
    Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(string query, int? year, CancellationToken ct = default);
    Task<TitleDetails?> GetMovieDetailsAsync(int tmdbId, CancellationToken ct = default);
    Task<TitleDetails?> GetShowDetailsAsync(int tmdbId, CancellationToken ct = default);
    Task<CatalogDetails?> GetCatalogDetailsAsync(int tmdbId, MediaKind mediaType, CancellationToken ct = default);
}

/// <summary>
/// Real TMDB implementation. Get a free API key (v3 auth) at
/// https://www.themoviedb.org/settings/api -- it must be exactly 32
/// hex characters; a truncated key (common copy-paste mistake) fails
/// every request with a 401.
/// </summary>
public class TmdbMetadataProvider : IMetadataProvider
{
    private const string ImageBase = "https://image.tmdb.org/t/p/w500";
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public TmdbMetadataProvider(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // Manual GetAsync + explicit status check (instead of GetFromJsonAsync)
    // so a failure includes TMDB's actual error body -- "Invalid API key:
    // You must be granted a valid key" is a lot more actionable than a
    // bare "401 Unauthorized" with no context.
    private async Task<T?> GetJsonOrThrowAsync<T>(string url, CancellationToken ct)
    {
        var raw = await GetRawOrThrowAsync(url, ct);
        // JsonSerializerDefaults.Web keeps the same case-insensitive
        // matching ReadFromJsonAsync used (TMDB sends lowercase keys).
        return System.Text.Json.JsonSerializer.Deserialize<T>(
            raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    // Same error handling, but hands back the untouched response body so
    // callers that stash raw JSON (GetCatalogDetailsAsync -> TmdbRawDetails)
    // don't pay for a second round-trip.
    private async Task<string> GetRawOrThrowAsync(string url, CancellationToken ct)
    {
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"TMDB request failed ({(int)resp.StatusCode} {resp.StatusCode}): {body}");
        }
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<MovieMetadata?> SearchMovieAsync(string title, int? year, CancellationToken ct = default)
    {
        var url = $"search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}"
                   + (year is not null ? $"&year={year}" : "");
        var result = await GetJsonOrThrowAsync<TmdbMovieSearchResponse>(url, ct);
        var match = result?.Results?.FirstOrDefault();
        if (match is null) return null;

        return new MovieMetadata(
            TmdbId: match.Id,
            Title: match.Title ?? title,
            Year: TryParseYear(match.ReleaseDate),
            PosterUrl: match.PosterPath is not null ? ImageBase + match.PosterPath : null,
            Overview: match.Overview,
            GenreIds: null // Skip genre fetch to avoid doubling TMDB calls during scan
        );
    }

    public async Task<ShowMetadata?> SearchShowAsync(string title, CancellationToken ct = default)
    {
        var url = $"search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}";
        var result = await GetJsonOrThrowAsync<TmdbTvSearchResponse>(url, ct);
        var match = result?.Results?.FirstOrDefault();
        if (match is null) return null;

        return new ShowMetadata(
            TmdbId: match.Id,
            Title: match.Name ?? title,
            PosterUrl: match.PosterPath is not null ? ImageBase + match.PosterPath : null,
            Overview: match.Overview,
            GenreIds: null // Skip genre fetch to avoid doubling TMDB calls during scan
        );
    }

    /// <summary>Candidate list for the admin Match modal (movies + TV, ~10 total).</summary>
    public async Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(string query, int? year, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var yearParam = year is not null ? $"&year={year}" : "";
        var airYearParam = year is not null ? $"&first_air_date_year={year}" : "";

        var movieTask = GetJsonOrThrowAsync<TmdbMovieSearchResponse>($"search/movie?api_key={_apiKey}&query={encoded}{yearParam}", ct);
        var tvTask = GetJsonOrThrowAsync<TmdbTvSearchResponse>($"search/tv?api_key={_apiKey}&query={encoded}{airYearParam}", ct);
        await Task.WhenAll(movieTask, tvTask);
        var movieResponse = await movieTask;
        var tvResponse = await tvTask;

        return (movieResponse?.Results?.Select(m => new TitleSearchResult(
                "Movie", m.Id, m.Title ?? query, TryParseYear(m.ReleaseDate),
                m.PosterPath is not null ? ImageBase + m.PosterPath : null, m.Overview))
                ?? Enumerable.Empty<TitleSearchResult>())
            .Concat(tvResponse?.Results?.Select(t => new TitleSearchResult(
                "Show", t.Id, t.Name ?? query, TryParseYear(t.FirstAirDate),
                t.PosterPath is not null ? ImageBase + t.PosterPath : null, t.Overview))
                ?? Enumerable.Empty<TitleSearchResult>())
            .Take(10)
            .ToList();
    }

    public async Task<TitleDetails?> GetMovieDetailsAsync(int tmdbId, CancellationToken ct = default)
    {
        var url = $"movie/{tmdbId}?api_key={_apiKey}&append_to_response=credits";
        var m = await GetJsonOrThrowAsync<TmdbMovieDetailsResponse>(url, ct);
        if (m is null) return null;

        var director = m.Credits?.Crew?.FirstOrDefault(c => c.Job == "Director")?.Name;
        var cast = m.Credits?.Cast?.Take(6).Select(c => c.Name).Where(n => n is not null).Select(n => n!).ToList() ?? new();

        return new TitleDetails(
            TmdbId: m.Id,
            Title: m.Title ?? "",
            Overview: m.Overview,
            PosterUrl: m.PosterPath is not null ? ImageBase + m.PosterPath : null,
            Director: director,
            Creators: null,
            Cast: cast
        );
    }

    public async Task<TitleDetails?> GetShowDetailsAsync(int tmdbId, CancellationToken ct = default)
    {
        var url = $"tv/{tmdbId}?api_key={_apiKey}&append_to_response=credits";
        var t = await GetJsonOrThrowAsync<TmdbTvDetailsResponse>(url, ct);
        if (t is null) return null;

        var creators = t.CreatedBy?.Select(c => c.Name).Where(n => n is not null).Select(n => n!).ToList() ?? new();
        var cast = t.Credits?.Cast?.Take(6).Select(c => c.Name).Where(n => n is not null).Select(n => n!).ToList() ?? new();

        return new TitleDetails(
            TmdbId: t.Id,
            Title: t.Name ?? "",
            Overview: t.Overview,
            PosterUrl: t.PosterPath is not null ? ImageBase + t.PosterPath : null,
            Director: null,
            Creators: creators,
            Cast: cast,
            GenreIds: t.Genres?.Select(g => g.Id).Where(id => id > 0).ToList()
        );
    }

    /// <summary>
    /// One round-trip for everything the catalog filters need: the details
    /// endpoint with release_dates/content_ratings AND credits appended
    /// (US cert, studios/networks, director/creators, top-30 cast). The
    /// raw response body rides along in CatalogDetails.RawJson so the
    /// backfill can stash it for future field expansions without a second
    /// TMDB call. Throws on TMDB failure -- the backfill's per-item catch
    /// leaves the DB columns NULL so the row retries on the next pass.
    /// </summary>
    public async Task<CatalogDetails?> GetCatalogDetailsAsync(int tmdbId, MediaKind mediaType, CancellationToken ct = default)
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        if (mediaType == MediaKind.Movie)
        {
            const string requestedWith = "release_dates,credits";
            var url = $"movie/{tmdbId}?api_key={_apiKey}&append_to_response={requestedWith}";
            var raw = await GetRawOrThrowAsync(url, ct);
            var m = System.Text.Json.JsonSerializer.Deserialize<TmdbMovieDetailsResponse>(raw, web);
            if (m is null) return null;

            var usRating = m.ReleaseDates?.Results?
                .FirstOrDefault(c => c.Iso31661 == "US")?.ReleaseDates?
                .Select(r => r.Certification)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

            var director = m.Credits?.Crew?.FirstOrDefault(c => c.Job == "Director")?.Name;

            return new CatalogDetails(
                GenreIds: m.Genres?.Select(g => g.Id).Where(id => id > 0).ToList() ?? new(),
                Rating: NullIfBlank(usRating),
                ProductionCompanies: CleanNames(m.ProductionCompanies?.Select(c => c.Name)),
                Networks: null,
                Director: NullIfBlank(director),
                Creators: null,
                Cast: TakeCast(m.Credits?.Cast),
                RawJson: raw,
                RequestedWith: requestedWith);
        }
        else
        {
            const string requestedWith = "content_ratings,credits";
            var url = $"tv/{tmdbId}?api_key={_apiKey}&append_to_response={requestedWith}";
            var raw = await GetRawOrThrowAsync(url, ct);
            var t = System.Text.Json.JsonSerializer.Deserialize<TmdbTvDetailsResponse>(raw, web);
            if (t is null) return null;

            var usRating = t.ContentRatings?.Results?
                .FirstOrDefault(c => c.Iso31661 == "US")?.Rating;

            return new CatalogDetails(
                GenreIds: t.Genres?.Select(g => g.Id).Where(id => id > 0).ToList() ?? new(),
                Rating: NullIfBlank(usRating),
                ProductionCompanies: null,
                Networks: CleanNames(t.Networks?.Select(n => n.Name)),
                Director: null,
                Creators: CleanNames(t.CreatedBy?.Select(c => c.Name)),
                Cast: TakeCast(t.Credits?.Cast),
                RawJson: raw,
                RequestedWith: requestedWith);
        }
    }

    // Top cast in TMDB billing order -- 30 is enough to catch notable
    // supporting roles (the free-text search matches these names) without
    // bloating the stored row. The detail modal keeps its own live
    // top-6 fetch for display.
    private const int StoredCastSize = 30;

    private static List<string>? TakeCast(List<TmdbCastMember>? cast)
    {
        var names = cast?.Select(c => c.Name)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Select(n => n!.Trim())
                         .Take(StoredCastSize)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();
        return names is { Count: > 0 } ? names : null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string>? CleanNames(IEnumerable<string?>? names) =>
        names?.Where(n => !string.IsNullOrWhiteSpace(n))
              .Select(n => n!.Trim())
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();

    private static int? TryParseYear(string? releaseDate) =>
        DateTime.TryParse(releaseDate, out var d) ? d.Year : null;

    private record TmdbMovieSearchResponse(List<TmdbMovie>? Results);
    private record TmdbTvSearchResponse(List<TmdbTvShow>? Results);
    private record TmdbMovie(
        int Id,
        string? Title,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        string? Overview);

    private record TmdbTvShow(
        int Id,
        string? Name,
        [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        string? Overview);

    private record TmdbMovieDetailsResponse(
        int Id,
        string? Title,
        string? Overview,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("genres")] List<TmdbGenre>? Genres,
        [property: JsonPropertyName("production_companies")] List<TmdbCompany>? ProductionCompanies,
        [property: JsonPropertyName("release_dates")] TmdbReleaseDatesResponse? ReleaseDates,
        TmdbCredits? Credits);

    private record TmdbTvDetailsResponse(
        int Id,
        string? Name,
        string? Overview,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("genres")] List<TmdbGenre>? Genres,
        [property: JsonPropertyName("networks")] List<TmdbNetwork>? Networks,
        [property: JsonPropertyName("content_ratings")] TmdbContentRatingsResponse? ContentRatings,
        TmdbCredits? Credits,
        [property: JsonPropertyName("created_by")] List<TmdbPerson>? CreatedBy);
    private record TmdbGenre(int Id, string? Name);
    private record TmdbCompany(string? Name);
    private record TmdbNetwork(string? Name);

    // /movie/{id}/release_dates (appended via append_to_response)
    private record TmdbReleaseDatesResponse(List<TmdbReleaseDateCountry>? Results);
    private record TmdbReleaseDateCountry(
        [property: JsonPropertyName("iso_3166_1")] string? Iso31661,
        [property: JsonPropertyName("release_dates")] List<TmdbReleaseDate>? ReleaseDates);
    private record TmdbReleaseDate(string? Certification);

    // /tv/{id}/content_ratings (appended via append_to_response)
    private record TmdbContentRatingsResponse(List<TmdbContentRatingCountry>? Results);
    private record TmdbContentRatingCountry(
        [property: JsonPropertyName("iso_3166_1")] string? Iso31661,
        string? Rating);

    private record TmdbCredits(List<TmdbCastMember>? Cast, List<TmdbCrewMember>? Crew);
    private record TmdbCastMember(string? Name);
    private record TmdbCrewMember(string? Name, string? Job);
    private record TmdbPerson(string? Name);
}

/// <summary>
/// Offline stand-in for tests/demos.
/// </summary>
public class FakeMetadataProvider : IMetadataProvider
{
    private readonly Dictionary<string, MovieMetadata> _movies;
    private readonly Dictionary<string, ShowMetadata> _shows;

    public FakeMetadataProvider(
        Dictionary<string, MovieMetadata> movies,
        Dictionary<string, ShowMetadata>? shows = null)
    {
        _movies = movies;
        _shows = shows ?? new Dictionary<string, ShowMetadata>();
    }

    public Task<MovieMetadata?> SearchMovieAsync(string title, int? year, CancellationToken ct = default)
    {
        _movies.TryGetValue(title, out var meta);
        return Task.FromResult(meta);
    }

    public Task<ShowMetadata?> SearchShowAsync(string title, CancellationToken ct = default)
    {
        _shows.TryGetValue(title, out var meta);
        return Task.FromResult(meta);
    }

    public Task<IReadOnlyList<TitleSearchResult>> SearchTitlesAsync(string query, int? year, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TitleSearchResult>>(Array.Empty<TitleSearchResult>());

    public Task<TitleDetails?> GetMovieDetailsAsync(int tmdbId, CancellationToken ct = default) =>
        Task.FromResult<TitleDetails?>(null);

    public Task<TitleDetails?> GetShowDetailsAsync(int tmdbId, CancellationToken ct = default) =>
        Task.FromResult<TitleDetails?>(null);

    public Task<CatalogDetails?> GetCatalogDetailsAsync(int tmdbId, MediaKind mediaType, CancellationToken ct = default) =>
        Task.FromResult<CatalogDetails?>(null);
}
