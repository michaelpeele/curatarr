# Research — Server-side paging & filtering for the library grid

**Status:** research only — **not started**. Scheduled **after** Sprint 4 #1 + Sprint 5 #6
(media-type filter / hide-per-library + card action icons), because both touch
`Curatarr.Web/src/components/PosterGrid.tsx`.
**Roadmap reference:** `ROADMAP.md` → Sprint 5 → item **8. Server-side paging & filtering**.
**Author:** Curatarr · 2026-11-17

---

## 1. Problem statement

Sprint 5 #7 shipped card windowing (`LazyMount`) and lazy poster loading
(`loading="lazy"`, TMDB `w185` thumbs). That fixed the DOM/memory blow-up Safari was killing the
tab over, but the page still **downloads and parses the entire catalog** before rendering anything.

Measured on prod (Kids Movies, 2026-11-17):

| Metric | Value |
|---|---|
| Rows returned by `GET /api/source-items` | **82,490** |
| Source items held in React state | 82,490 |
| Entries built client-side (movies + grouped shows) | 3,987 |
| Poster images actually mounted after windowing | 3 at rest, 6 after 19k px scroll |
| Nodes in DOM after windowing | ~16,000 |

So windowing solved rendering, and the remaining lag is **transfer + JSON parse + grouping +
filtering** of the full catalog on every load (and on every reload triggered by
`refreshKey`/reconcile/backfill).

Goal: render the first screen from a **small paged response** (≤ 200 entries), push
search/genre/rating/studio/network filters to the server, and load more on scroll.

---

## 2. Current implementation map

### Backend
- `Curatarr.Api/Program.cs` lines ~567-618: `GET /api/source-items?libraryId=`
  - Loads **all** `SourceItems`, projects 18 fields:
    `Id, MediaType, Title, Year, ShowTitle, SeasonNumber, EpisodeNumber, TmdbId, PosterUrl,
    Overview, RelativePath, GenreIds, Rating, ProductionCompanies, Networks, Director,
    Creators, Cast`.
  - With `libraryId`, annotates `Approved`, `ApprovedAt`, `Hidden` (joins `VirtualLibraryItems`
    and `HiddenItems` for that library).
  - No `limit`/`offset`, no filters, no search.
- `Curatarr.Engine/Data/Models.cs`:
  - `SourceItem.GenreIds` is a **comma-joined string** (e.g. `"35,10751"`) with a splitter helper
    at lines ~74-76.
  - `ProductionCompanies` and `Networks` are **pipe-joined strings**.
- Endpoint registration pattern to copy: `Curatarr.Api/PlexEndpoints.cs:16`
  `public static void MapPlexEndpoints(this WebApplication app)` registered by
  `app.MapPlexEndpoints();` in `Program.cs:170`.

### Frontend (`Curatarr.Web/src/components/PosterGrid.tsx`)
- `api.listSourceItems()` (5 call sites: initial load, single-item refresh, post-backfill,
  post-reconcile) → `items: SourceItem[]` state.
- `entries` memo: filters `items` by `libraryMediaType` (Movie ⇒ no season/episode; otherwise
  requires season/episode), then by `genreFilter` (`GenreIds`), `ratingFilter`, `studioFilter`
  (`ProductionCompanies`), `networkFilter` (`Networks`), and `searchTokens`
  (title + director + cast for movies; group title + creators + cast for shows).
- Movies map to `{kind:"movie"}`; non-movies are grouped by `buildShowGroups()` from
  `Curatarr.Web/src/lib/showGroups.ts` and map to `{kind:"show"}`.
- Grouped entries are bucketed into letter sections (`letterFor(label)` → `A-Z` or `#`) with a
  right-hand letter rail that `jumpTo()`s anchors.
- Facet dropdown options (`ratingOptions`, `studioOptions`, `networkOptions`) are computed from
  the **full** in-memory entry list.
- `showGroups.ts` grouping contract that the server must reproduce:
  - `showGroupKey(item)`: `"{mediaType}:tmdb:{tmdbId}"` when `tmdbId > 0`, else
    `"{mediaType}:title:{showTitle.toLowerCase()}"` when `showTitle` is non-blank, else
    `"{mediaType}:id:{id}"`.
  - `episodeKey(item)`: `S{season}E{episode}` when both present, else `path:{relativePath}`, else id.
  - Group display fields filled from the **first non-empty** episode value: poster, overview,
    rating, networks, creators, cast.
  - `seasonCount` = distinct seasons > 0; `episodeCount` = distinct `episodeKey`;
    `latestSeason`/`latestEpisode` = max by season then episode.
  - `representativeId` = first episode with a poster, else first episode.

---

## 3. Proposed architecture

### 3.1 `GET /api/library-entries`

```
GET /api/library-entries
    ?libraryId=<id>          (optional — membership-gated annotation when present)
    &mediaType=Movie|Show|Anime
    &q=<free text>
    &genre=<tmdb genre id>   (repeatable)
    &rating=<rating string>
    &studio=<company name>
    &network=<network name>
    &letter=A..Z|#
    &offset=0&limit=60       (limit clamped 1..200)
```

Response:

```jsonc
{
  "total": 3987,          // total entries matching the filters, before paging
  "offset": 0,
  "limit": 60,
  "hasMore": true,
  "mediaType": "Movie",
  "entries": [
    { "kind": "movie", "id": "...", "title": "...", "posterUrl": "...", "year": 1995,
      "rating": "PG", "genreIds": [35,10751], "approved": false, "approvedAt": null, "hidden": false },
    { "kind": "show", "key": "Show:tmdb:1234", "title": "...", "posterUrl": "...",
      "seasonCount": 3, "episodeCount": 42, "latestSeason": 3, "latestEpisode": 11,
      "representativeId": "...", "approved": true, "approvedAt": "...", "hidden": false,
      "episodeIds": ["...", "..."] }
  ]
}
```

Notes:
- `entries` are **already grouped**: movies are one entry per `SourceItem`; shows/anime are one
  entry per `showGroupKey`.
- `episodeIds` is capped (e.g. first 200) purely so the details modal can resolve an episode
  without another round trip; the modal can also lazily fetch the full set.
- `approved` for a show entry is true when **any** episode is approved; `approvedAt` is the
  newest `ApprovedAt`; `hidden` is true when **all** episodes are hidden.

### 3.2 `GET /api/library-facets`

```
GET /api/library-facets?mediaType=Movie|Show|Anime
```

Returns the distinct filter values so the dropdowns no longer need the full catalog:

```jsonc
{
  "ratings": ["G", "PG", "PG-13", "R"],
  "studios": ["Walt Disney Pictures", "Pixar"],
  "networks": ["Disney Channel", "PBS"],
  "genres": [{ "id": 16, "count": 412 }, { "id": 35, "count": 1203 }]
}
```

Computed with `SELECT DISTINCT` over the same filtered scope (no `q`/`letter`), ordered by the
existing `MOVIE_RATING_ORDER` / `TV_RATING_ORDER` constants (ported to C#) and then alphabetically.
`genres` counts come from splitting `GenreIds`; the frontend already owns the id → name map.

### 3.3 Query plan (must stay O(page), not O(catalog))

1. **Candidate projection** — select only the columns needed for filtering + grouping + sorting:
   `Id, MediaType, Title, Year, ShowTitle, SeasonNumber, EpisodeNumber, TmdbId, PosterUrl,
   Rating, GenreIds, ProductionCompanies, Networks, Director, Creators, Cast`.
   Push `mediaType` down to SQL (`Movie` ⇒ `SeasonNumber == null && EpisodeNumber == null`;
   otherwise the inverse). Push `rating`/`studio`/`network` to SQL as `Contains`/`StartsWith`
   with the same delimiter rules (`|` for companies/networks).
2. **Group** surviving rows in memory by `ShowGroupKey` (port of `showGroups.ts`) — this is the
   only unavoidable in-memory step because EF/SQLite cannot group by the composite key cheaply.
3. **Apply `q` and `letter`** against the grouped entries (title/creators/cast, then first letter).
4. **Sort** by title (`OrdinalIgnoreCase`), compute `total`, then `Skip(offset).Take(limit)`.
5. **Hydrate** only the page's entries with the full 18-field projection + approved/hidden
   annotation (single `WHERE Id IN (...)` query, or `WHERE VirtualLibraryId = @lib AND SourceItemId IN (...)`).

If step 2 becomes the bottleneck at 82k rows, cache the grouped entry list in a
`MemoryCache` keyed by `(sourceRootVersion, libraryId)` and invalidate on scan/reconcile —
**only add caching if profiling shows it is needed**; measure first.

### 3.4 Filter semantics (parity table)

| Control | Current client behaviour | Server implementation |
|---|---|---|
| View filter (S4 #1) | `All/Library/Unmarked/Hidden` after hidden is wired | `view=all\|library\|unmarked\|hidden` applied to annotated page; hidden excluded unless `view=hidden` |
| Genre | `GenreIds` split on `,` contains id | `("," + GenreIds + ",").Contains("," + id + ",")` (or split after fetch) |
| Rating | exact string match | `Rating == value` |
| Studio | split `ProductionCompanies` on `\|`, exact match | `ProductionCompanies != null && ("\|" + ProductionCompanies + "\|").Contains("\|" + value + "\|")` |
| Network | split `Networks` on `\|`, exact match | same pipe rule |
| Search | tokens ANDed over haystack | tokens ANDed over a computed haystack column (title + creator/director/cast) |
| Letter | first char of label, `#` for non A-Z | computed after grouping, filtered in memory |
| Sort | alphabetical by title | `OrderBy(title, StringComparer.OrdinalIgnoreCase)` |

---

## 4. Required backend updates (checklist)

- [ ] **New file** `Curatarr.Api/LibraryGridEndpoints.cs`
  - `public static void MapLibraryGridEndpoints(this WebApplication app)`
  - `GET /api/library-entries` handler (params above) — `RequireAuthorization()` (all roles);
        membership annotation only when `libraryId` is supplied and the caller can see it.
  - `GET /api/library-facets` handler — `RequireAuthorization()`.
- [ ] `Curatarr.Api/Program.cs`
  - Register `app.MapLibraryGridEndpoints();` next to `app.MapPlexEndpoints();` (line ~170).
  - Leave `GET /api/source-items` untouched for now (still used by Sources/Activity pages).
- [ ] **New file** `Curatarr.Engine/LibraryGrid/ShowGrouping.cs`
  - Pure static port of `showGroups.ts`: `ShowGroupKey`, `EpisodeKey`, `Group` builder,
    `RepresentativeId`, `LatestSeason/Episode`, `SeasonCount`, `EpisodeCount`.
  - Must be usable without a DbContext so it is unit-testable directly.
- [ ] **New file** `Curatarr.Engine/LibraryGrid/GridFilters.cs`
  - Pure static filter predicates: `InMediaType`, `MatchesGenre`, `MatchesRating`,
    `MatchesStudio`, `MatchesNetwork`, `MatchesTokens`, `FirstLetter`.
  - Delimiter-aware helpers: `SplitPipe`, `SplitGenres`, `ContainsToken`.
- [ ] `Curatarr.Engine/Data/Models.cs` — no schema change expected. If `q` search proves slow,
      consider a non-unique index on `SourceItems.Title` (EF migration required; follow the
      migration-file convention: main `.cs` has no attributes, `[DbContext]`/`[Migration]` live in
      the `.Designer.cs`).
- [ ] Pagination helpers: clamp `limit` to 1..200 (default 60), reject `offset < 0`,
      return `hasMore = offset + entries.Count < total`.

---

## 5. Required frontend updates (checklist)

- [ ] `Curatarr.Web/src/api/client.ts`
  - `listLibraryEntries(params)` → typed `LibraryEntriesPage`
  - `listLibraryFacets(mediaType)` → typed `LibraryFacets`
  - Reuse the existing `request<T>()` so `{ error }` messages still surface.
- [ ] `Curatarr.Web/src/components/PosterGrid.tsx`
  - Replace `items: SourceItem[]` with `entries: GridEntry[]` + `total` + `loadingMore` + `offset`.
  - Re-fetch page 0 (debounced 250 ms) whenever `libraryId`, `mediaType`, view filter, genre,
        rating, studio, network, `letter`, or `searchQuery` changes; reset `offset`.
  - Append the next page when the sentinel near the bottom enters the viewport
        (`IntersectionObserver` — same pattern as `LazyMount`), guard with `hasMore`/`loadingMore`.
  - Keep `LazyMount`, `posterThumb`, the letter rail, and the approve/hide handlers unchanged;
        the rail now navigates by **letter filter** rather than DOM anchor.
  - Drop `buildShowGroups` / `showGroupKey` / `episodeKey` imports from `PosterGrid.tsx`
        (still used by the details modal for per-episode lists) and remove the
        `ratingOptions`/`studioOptions`/`networkOptions` memos in favour of server facets.
  - Preserve optimistic mark/hide updates: patch the matching entry in `entries` by
        `kind === "movie" ? id : key`.
- [ ] `Curatarr.Web/src/lib/showGroups.ts`
  - Keep the exported helpers (details modal + tests still use them), but mark the grouping
        entry point as superseded by the server response.
- [ ] Loading states: skeleton rows for the first page (reuse the `LazyMount` skeleton styling),
        a small "Loading more…" row at the sentinel, and an empty state when `total === 0`.

---

## 6. Compatibility & rollout

- Feature flag `VITE_LIBRARY_PAGING` (default on) with the old `listSourceItems()` path
  retained behind it for one release, so the grid can be compared side by side on the 82k catalog.
- The old endpoint stays; nothing else consumes `listLibraryEntries`.
- Ship order: backend first (endpoints + unit tests green, curl-verified), then frontend behind
  the flag, then flip the default once the manual acceptance checks pass.

---

## 7. Edge cases & risks

1. **Grouping parity** — server grouping must produce identical `key`/scores to `showGroups.ts`,
   otherwise `representativeId`-based approve/hide lookups drift. Mitigation: shared test vectors
   (same JSON fixtures asserted in C# and TS).
2. **`approved` for groups** — "any episode approved" vs "all episodes approved" changes the
   Unmarked view. Decision: entry is `approved` when any episode is approved; `hidden` only when
   all are. Record the decision in the UI tooltip.
3. **`letter` + `q` + paging** — filtering must be applied *before* `total` is computed, or the
   letter rail will show empty sections.
4. **Schedule-inactive libraries** — entries still render; only symlinks/Plex are gated. Do not
   filter by schedule server-side.
5. **SignalR live scan** — new items must invalidate the cached/grouped list; simplest is to
   re-fetch page 0 when a scan batch completes, keeping the current optimistic prepend for the
   visible page.
6. **Deep links / letter anchors** — `#letter-M` anchors no longer exist once the grid is paged;
   replace with `?letter=M` state and keep the URL shareable if it was.
7. **SQLite `Contains` performance** — pipe/comma delimiter matching prevents index use. Acceptable
   at 82k rows; if not, move to normalized join tables (`SourceItemGenres`, `SourceItemCompanies`)
   in a later migration. **Measure before normalizing.**
8. **Membership gating** — `libraryId` annotation must reuse the approve endpoint's rule
   (Admin bypass, Owner/Curator need a membership row, Viewer read-only is fine).

---

## 8. Unit test plan

There is currently **no test project** in the repo (`*.csproj`: `Curatarr.Api`, `Curatarr.Engine`,
`Curatarr.EngineDemo`). Add one:

- [ ] **New project** `Curatarr.Tests/Curatarr.Tests.csproj` — xUnit + `Microsoft.NET.Test.Sdk`
      + `FluentAssertions` (optional), `ProjectReference` to `Curatarr.Engine` and `Curatarr.Api`.
      Run with `dotnet test` inside the SDK container (`docker run --rm -v $PWD:/src -w /src
      mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`) — `dotnet` is not installed on the host.
- [ ] Add `Curatarr.Tests` to the solution/build (or a `docker compose` test target).

### 8.1 Grouping (`ShowGrouping`)

1. Two episodes with the same `TmdbId` and media type `Show` → one group, key `Show:tmdb:{id}`.
2. Missing `TmdbId` (0) but a `ShowTitle` → key `Show:title:{lowercased title}`.
3. Missing `TmdbId` and blank `ShowTitle` → key `Show:id:{id}` (never merged with siblings).
4. Same `TmdbId` but different `MediaType` (`Show` vs `Anime`) → **two** groups.
5. Poster/overview/rating/networks/creators/cast come from the first non-empty episode, not the
   first episode.
6. `episodeCount` de-duplicates by `S{season}E{episode}` (two rows, same episode → count 1).
7. `episodeCount` falls back to `path:{relativePath}` when season/episode are missing.
8. `seasonCount` ignores season 0 / null seasons.
9. `latestSeason`/`latestEpisode` pick the highest season then highest episode.
10. `representativeId` is the first episode that has a poster; falls back to the first episode.
11. Groups are sorted by title, case-insensitively, and `"10 Lives"` sorts before `"9"`-style
        numeric comparisons that a naive lexical sort would break (document the chosen order).

### 8.2 Filter predicates (`GridFilters`)

12. `MatchesGenre("35,10751", 35)` → true; `MatchesGenre("35,10751", 3)` → false
        (guards against `3` matching inside `35`).
13. `MatchesGenre(null, 35)` → false; `MatchesGenre("", 35)` → false.
14. `MatchesStudio("Pixar|Walt Disney Pictures", "Pixar")` → true; `"Pi"` → false (no prefix match).
15. `MatchesNetwork(null, x)` → false; `MatchesNetwork("PBS|NBC", "NBC")` → true.
16. `MatchesRating("PG", "PG")` → true; `MatchesRating("PG-13", "PG")` → false.
17. `InMediaType(Movie row, "Movie")` → true; an episode row with `SeasonNumber` set → false.
18. `InMediaType(episode row, "Show")` → true; a Movie row → false.
19. `MatchesTokens("Cinderella", ["cind"])` → true; `["cind", "zzz"]` → false (AND semantics).
20. `MatchesTokens` is case-insensitive and trims/ignores empty tokens.
21. `FirstLetter("10 Lives")` → `"#"`; `FirstLetter("a bug's life")` → `"A"`; `FirstLetter("")` → `"#"`.

### 8.3 Paging (`LibraryEntriesQuery`)

22. `limit` defaults to 60, clamps to 1..200; `offset < 0` → 400 or clamped to 0 (pick one and test it).
23. `total` is the filtered count **before** paging; `hasMore` is true while
        `offset + returned < total`.
24. Page 2 (`offset=60, limit=60`) returns entries 61-120 with the same ordering as page 1.
25. Last page returns fewer than `limit` entries and `hasMore = false`.
26. `offset` beyond `total` returns an empty `entries` array with `hasMore = false` (not a 404).
27. Ordering is stable across pages (no duplicate/skipped entries) when titles tie — ties broken
        by `Id`.

### 8.4 Annotation (approvals / hidden)

28. No `libraryId` → every entry `approved=false`, `approvedAt=null`, `hidden=false`.
29. Movie with an approval row → `approved=true` and the exact `ApprovedAt`.
30. Show group where one episode is approved → group `approved=true`, `approvedAt` = newest.
31. Show group where all episodes are hidden → `hidden=true`; one hidden of many → `hidden=false`.
32. Hidden item still appears when the view filter is `all`? — **No**: hidden entries are excluded
        from `all`/`library`/`unmarked` and only returned by `view=hidden`.

### 8.5 Facets

33. `ratings` are de-duplicated and ordered by `MOVIE_RATING_ORDER`/`TV_RATING_ORDER`, then alphabetically.
34. `studios` split `ProductionCompanies` on `|` and de-duplicate (`"Pixar|Pixar"` → one entry).
35. `networks` are only emitted for non-Movie media types.
36. Facets ignore `q`/`letter`/`offset`/`limit` but honour `mediaType` (so the dropdowns don't
        shrink as you page).

### 8.6 Endpoint integration tests (`WebApplicationFactory<Program>`)

37. `GET /api/library-entries` without auth → 401.
38. `?limit=500` → `limit` echoed as 200.
39. `?mediaType=Bogus` → 400 with `{ error }`.
40. `?libraryId=` unknown id → 404 (not a 500).
41. Viewer role → annotations returned but no write access implied (read remains 200).
42. Response payload for `limit=60` on the 82k fixture is < 60 KB (regression guard).

### 8.7 Frontend tests (optional, if a harness is added)

43. Debounce: rapid `searchQuery` changes issue one request.
44. Sentinel triggers exactly one `offset += limit` fetch; no duplicate requests while loading.
45. Filter change resets `offset` to 0 and clears accumulated entries.
46. Optimistic mark/hide patching keeps the entry's position (no scroll jump).

---

## 9. Acceptance criteria

- First screen renders from **one** request of ≤ 60 entries; total transfer for the initial view
  drops from ~16-30 MB to < 100 KB.
- Scrolling to the bottom appends pages without duplicate or missing cards.
- Search, genre, rating, studio, network and letter filters return the same results as the current
  client-side implementation on the 82k catalog (spot-check 10 known titles per filter).
- All unit tests in §8.1-§8.6 green; `docker compose build api` and `docker compose build web` green.
- Safari soak: scroll the full Kids Movies grid for 5 minutes with no "page reloaded due to memory"
  warning and flat memory.

---

## 10. Out of scope (later)

- Normalizing genres/companies/networks into join tables (only if §3.3 step 2/profile demands it).
- Server-side sorting options (year, added date, rating) beyond title.
- Caching the grouped entry list across requests.
- Reworking the details modal's episode list (it may keep using `buildShowGroups` client-side).
