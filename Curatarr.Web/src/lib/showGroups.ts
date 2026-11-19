import type { SourceItem } from "../api/client";

/**
 * A show is not a database entity in Curatarr v1 -- the scanner stores one
 * SourceItem row per episode. The UI groups those rows into a ShowGroup so
 * a 10-episode show renders as one poster instead of 10.
 */
export interface ShowGroup {
  key: string;
  title: string;
  mediaType: "Show" | "Anime";
  posterUrl: string | null;
  overview: string | null;
  tmdbId: number | null;
  /** TMDB genre IDs (comma-separated string from DB) */
  genreIds: string | null;
  /** First non-empty US content rating among the episodes ("TV-14", "" none, null not fetched) */
  rating: string | null;
  /** First non-empty "|" -separated network list among the episodes */
  networks: string | null;
  /** First non-empty "|" -separated creator list among the episodes */
  creators: string | null;
  /** First non-empty "|" -separated top-30 cast list among the episodes */
  cast: string | null;
  episodes: SourceItem[];
  episodeIds: string[];
  episodeCount: number;
  seasonCount: number;
  latestSeason: number | null;
  latestEpisode: number | null;
  representativeId: string;
}

function normalizeTitle(title: string | null | undefined): string {
  return (title ?? "").trim().toLowerCase();
}

export function isShowItem(item: SourceItem): boolean {
  return item.mediaType !== "Movie";
}

/**
 * Prefer the stable TMDB id when the scan enriched the show; otherwise fall
 * back to the normalized folder-derived title (TMDB is optional, so this
 * fallback is the normal path for keyless installs). The media type is part
 * of the key so the same title in a Show root and an Anime root stays
 * separate.
 */
export function showGroupKey(item: SourceItem): string {
  if (item.tmdbId != null && item.tmdbId > 0) {
    return `${item.mediaType}:tmdb:${item.tmdbId}`;
  }

  const title = normalizeTitle(item.showTitle);
  return title
    ? `${item.mediaType}:title:${title}`
    : `${item.mediaType}:id:${item.id}`;
}

/**
 * Episode identity for counting and dedupe. Duplicate copies of the same
 * episode in two roots count once; rows whose filename had no SxxEyy fall
 * back to the relative path (identical copies share a path), then the id.
 */
export function episodeKey(item: SourceItem): string {
  if (item.seasonNumber != null && item.episodeNumber != null) {
    return `S${item.seasonNumber}E${item.episodeNumber}`;
  }
  if (item.relativePath) {
    return `path:${item.relativePath.trim().toLowerCase()}`;
  }
  return item.id;
}

function isLaterEpisode(candidate: SourceItem, current: SourceItem): boolean {
  const candidateSeason = candidate.seasonNumber ?? -1;
  const candidateEpisode = candidate.episodeNumber ?? -1;
  const currentSeason = current.seasonNumber ?? -1;
  const currentEpisode = current.episodeNumber ?? -1;

  return (
    candidateSeason > currentSeason ||
    (candidateSeason === currentSeason && candidateEpisode > currentEpisode)
  );
}

/**
 * Superseded for the grid by GET /api/library-entries (VITE_LIBRARY_PAGING),
 * which returns pre-grouped show entries from the C# port of this file. Kept
 * for the legacy feature-flag fallback, the details modal's episode list and
 * the shared TS/C# grouping test vectors.
 */
export function buildShowGroups(items: SourceItem[]): ShowGroup[] {
  const groups = new Map<string, ShowGroup>();

  for (const item of items) {
    if (!isShowItem(item)) continue;

    const key = showGroupKey(item);
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        title: item.showTitle ?? "Untitled",
        mediaType: item.mediaType === "Anime" ? "Anime" : "Show",
        posterUrl: item.posterUrl,
        overview: item.overview,
        tmdbId: item.tmdbId,
        genreIds: item.genreIds,
        rating: item.rating,
        networks: item.networks,
        creators: item.creators,
        cast: item.cast,
        episodes: [],
        episodeIds: [],
        episodeCount: 0,
        seasonCount: 0,
        latestSeason: null,
        latestEpisode: null,
        representativeId: item.id,
      };
      groups.set(key, group);
    }

    group.episodes.push(item);
    group.episodeIds.push(item.id);
    if (!group.posterUrl && item.posterUrl) group.posterUrl = item.posterUrl;
    if (!group.overview && item.overview) group.overview = item.overview;
    // First non-empty wins ("" = fetched-but-none, keep looking)
    if (!group.rating && item.rating) group.rating = item.rating;
    if (!group.networks && item.networks) group.networks = item.networks;
    if (!group.creators && item.creators) group.creators = item.creators;
    if (!group.cast && item.cast) group.cast = item.cast;
  }

  for (const group of groups.values()) {
    const seasonNumbers = new Set<number>();
    const episodeKeys = new Set<string>();
    let latest: SourceItem | null = null;

    for (const episode of group.episodes) {
      if (episode.seasonNumber != null && episode.seasonNumber > 0) {
        seasonNumbers.add(episode.seasonNumber);
      }
      episodeKeys.add(episodeKey(episode));

      if (episode.seasonNumber != null && episode.episodeNumber != null) {
        if (latest === null || isLaterEpisode(episode, latest)) latest = episode;
      }
    }

    const posterEpisode = group.episodes.find((episode) => !!episode.posterUrl);
    group.representativeId =
      posterEpisode?.id ?? group.episodes[0]?.id ?? group.representativeId;
    group.episodeCount = episodeKeys.size;
    group.seasonCount = seasonNumbers.size;
    group.latestSeason = latest?.seasonNumber ?? null;
    group.latestEpisode = latest?.episodeNumber ?? null;
  }

  return [...groups.values()].sort((a, b) => a.title.localeCompare(b.title));
}

/**
 * Pick the SourceItem id to approve as the representative for the whole
 * group. Since SymlinkEngine now creates one symlink per top-level show
 * folder, only one VirtualLibraryItem row is needed regardless of how
 * many episode rows exist under the group key. The representative is the
 * first row with a poster (best metadata), falling back to the first row.
 */
export function representativeId(group: ShowGroup): string {
  return group.representativeId ?? group.episodes[0]?.id ?? "";
}
