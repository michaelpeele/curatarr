using System.Globalization;
using System.Text.Json;
using Curatarr.Engine.Data;

namespace Curatarr.Api;

/// <summary>
/// Pulls calendar dates out of the raw TMDB payload already cached in
/// TmdbRawDetails.Json. Pure/local: no HTTP calls and no shared state, so it
/// can be used by both the foreground details backfill and the cached-row
/// sweep without changing either one's retry semantics.
/// </summary>
public static class CalendarDateParser
{
    public static void ApplyRawDates(SourceItem item, string? json, DateTime parsedAt)
    {
        // Always record the attempt, including an empty/invalid payload: a row
        // that had no date today should keep the stamp so the sweep doesn't
        // re-parse thousands of cached bodies on every startup.
        item.DateDetailsParsedAt = parsedAt;
        item.ReleaseDate = null;
        item.AirDate = null;
        item.AirSeasonNumber = null;
        item.AirEpisodeNumber = null;
        item.AirIsSeasonPremiere = false;

        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (item.MediaType == MediaKind.Movie)
            {
                if (root.TryGetProperty("release_date", out var release) &&
                    release.ValueKind == JsonValueKind.String)
                {
                    item.ReleaseDate = ParseDate(release.GetString());
                }
                return;
            }

            // Shows and anime are both cached as /tv/{id} responses. Episode
            // rows each carry the same show-level next_episode_to_air object;
            // the endpoint de-duplicates them before rendering the grid.
            if (!root.TryGetProperty("next_episode_to_air", out var next) ||
                next.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (next.TryGetProperty("air_date", out var air) &&
                air.ValueKind == JsonValueKind.String)
            {
                item.AirDate = ParseDate(air.GetString());
            }
            if (next.TryGetProperty("season_number", out var season) &&
                season.TryGetInt32(out var seasonNumber))
            {
                item.AirSeasonNumber = seasonNumber;
            }
            if (next.TryGetProperty("episode_number", out var episode) &&
                episode.TryGetInt32(out var episodeNumber))
            {
                item.AirEpisodeNumber = episodeNumber;
                // TMDB's next_episode_to_air is a season premiere when it is
                // episode 1. If that assumption ever changes, this is the one
                // place to adjust it.
                item.AirIsSeasonPremiere = episodeNumber == 1;
            }
        }
        catch (JsonException)
        {
            // Cached body wasn't usable JSON. parsedAt above stops endless
            // retries; DetailsBackfill will re-stash it if it refetches later.
        }
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
}
