# Calendar from metadata — implementation plan

**Status:** slice 1 (read-only foundation) promoted to prod at 15:09Z on 2026-11-18; slice 2 (All-view link/Hide modal) implemented, sandbox-verified and promoted to prod at 18:00Z on 2026-11-18. ROADMAP Sprint 4 #2 Calendar is checked off.

This is the first remaining grouped item (`REMAINING-WORK-GROUPS.md` Group A, mandated batch #6). It merges `ROADMAP.md:356` (Calendar from metadata) with its duplicate at `:179` and the date-model portion of `:162` (Show-level metadata). The canonical workflow in `/home/michaelpeele/ai-projects/workflow.md` applies: one shared tree, Docker-only builds, sandbox first, then promotion.

---

## 1. Decisions (confirmed by the user 2026-11-18)

1. **Date source:** add explicit date columns to `SourceItem` and backfill them from the existing `TmdbRawDetails.Json` cache. No live TMDB calls from the Calendar path.
2. **TV dates:** use `next_episode_to_air.air_date` when present. If that episode has `episode_number == 1`, show a `New Season (#)` badge. Past episodes/seasons are not separate calendar entries; shows with no next episode produce no event.
3. **Access:** all authenticated users, matching the current `App.tsx` guard and the absence of `adminOnly` on the sidebar entry.
4. **Timezone:** server timezone from `Schedules:TimeZone`, resolved by the injectable `ScheduleTimeZone` singleton. It determines "today" and therefore the default month; calendar dates themselves stay date-only (`yyyy-MM-dd`). The Calendar endpoint returns `timeZone` / `today` itself because `/api/schedules/settings` is admin-only and Calendar is available to all authenticated users.
5. **Link/Hide modal:** deferred to slice 2. It must reuse the library-page APIs, labels, and Sync warning when built.
6. **Slicing:** ship a read-only calendar foundation first. The user also confirmed the Calendar page gets **the same global toolbar and Sync behaviour as the library page** — that already comes from `App.tsx` rendering the shared `<BottomBar …>` for every view; no separate Calendar toolbar is needed.

Out of scope for this item: loading animation, quality-variant labels, responsive redesign, and infrastructure work.

---

## 2. Current state

- `Curatarr.Web/src/App.tsx:359-364` renders `ComingSoonPage` for `view === "calendar"` with the text *"Upcoming episode air dates, once TMDB air-date data is wired into the scanner."*
- `Curatarr.Web/src/components/Sidebar.tsx:27` has `{ key: "calendar", …, comingSoon: true }`; `NavButton` renders the soon badge.
- `App.tsx` already loads `libraries` / `selectedLibraryId` / `selectedLibMediaType`, tracks `posterRefreshKey` and `librariesVersion`, and passes all of it plus `onScan` / `onReconcile` to `<BottomBar>` (`:382-405`). Scan/Sync therefore already works for any page rendered inside `AuthedApp`.
- `Curatarr.Web/src/components/BottomBar.tsx` owns the global library selector, Scan, Sync, schedule button, and status toasts. Calendar will not duplicate these.
- `Curatarr.Engine/Data/Models.cs:39-102` `SourceItem` has `Year` but no release/air date fields. `TmdbRawDetails` (`:117-123`) stores the full raw TMDB response per `(mediaType, tmdbId)`.
- `Curatarr.Engine/MetadataProvider.cs:203-264` fetches movie details with `release_dates,credits` and TV details with `content_ratings,credits`; `CatalogDetails.RawJson` carries the body. `release_date` is already in the movie DTO/raw JSON; `next_episode_to_air` is present in the raw TV response but not surfaced by the typed DTOs.
- `Curatarr.Api/Program.cs:1695-1791` `DetailsBackfill` fetches details and upserts `TmdbRawDetails`. `Curatarr.Api/ScheduleEndpoints.cs:17-35` exposes the configured zone and `nowLocal`; `ScheduleTimeZone.cs` holds the singleton resolver.
- PosterGrid's Mark/Hide wording, two-phase hide state, and API calls are complete and will be reused verbatim in slice 2; nothing in slice 1 invents new approval/hide state.

---

## 3. Slice split

### Slice 1 — read-only calendar foundation (this update)

1. Add date columns to `SourceItem` + EF migration:
   - `ReleaseDate` (`TEXT`, `yyyy-MM-dd`, movies)
   - `AirDate` (`TEXT`, `yyyy-MM-dd`, shows from `next_episode_to_air`)
   - `AirSeasonNumber`, `AirEpisodeNumber` (`INTEGER`, nullable)
   - `AirIsSeasonPremiere` (`INTEGER`, bool; true when `episode_number == 1`)
   - `DateDetailsParsedAt` (`TEXT`, UTC sentinel; `null` = not parsed from raw JSON yet)
2. Add a pure parser (`CalendarDateParser`) that reads `TmdbRawDetails.Json` with `System.Text.Json` and returns the fields above. No HTTP calls.
3. Backfill:
   - Extend `DetailsBackfill.RunBatchAsync` to fill the date columns from the `RawJson` it already fetched, so newly backfilled rows are immediately calendar-ready.
   - Add a small hosted `CalendarDateBackfillService` that parses rows where `DateDetailsParsedAt == null` and a matching raw row exists. It retries unknown/missing raw rows on later passes; it never calls TMDB.
4. Add `GET /api/calendar/events` returning `{ timeZone, today, month, events }` for a `yyyy-MM` month, with `libraryId` and `mediaType` query filters. Enforce the same membership rules as the library grid: Admin sees all; non-admins only libraries they belong to; Viewers get no write powers but may read. A requested library the user cannot access returns 403/404 rather than leaking entries.
5. Add client types/methods to `client.ts`: calendar event DTO and `listCalendarEvents`. Do **not** add a schedule-settings getter — `/api/schedules/settings` is admin-only; the Calendar endpoint itself returns `timeZone` / `today`.
6. Add `CalendarPage.tsx`: month grid defaulting to `today` from the API, `‹`/`›` arrows, clickable `Month YYYY` heading that opens a native month/date picker jump, poster+title cells, `New Season (#)` badge, library and Movie/Show/Anime filters. Entries are read-only this slice.
7. Replace the `ComingSoonPage` in `App.tsx` with `<CalendarPage …>` and remove `comingSoon` from `Sidebar.tsx:27`. Keep the existing `<BottomBar>` wiring untouched.

**Slice 1 acceptance:** current month (server zone) by default; arrows and date-picker heading work; library + media-type filters work; movie releases and next-episode air dates appear on the right day with poster/title and New Season badge; shared toolbar/Scan/Sync still works; all authenticated users can open the page; no link/hide controls yet.

### Slice 2 — All-view link/Hide actions (done 2026-11-18, separate verification)

- In "All libraries" view only (`libraryFilter === ""`), clicking an event card opens a
  new `CalendarItemModal` with keyboard-accessible button semantics. Cards in a concrete
  library stay read-only in this slice.
- Extend the Calendar event DTO with `sourceItemIds: string[]` and
  `representativeSourceItemId: string | null`. Movies: the single movie SourceItem id.
  Shows/anime: all SourceItems behind the de-duplicated `(MediaType, TmdbId, date)` event,
  ordered with the metadata-bearing representative first.
- The modal shows poster/title/date and a library picker starting at
  `Choose a library…`; Mark/Hide actions stay disabled until a valid writable library is
  chosen. Writable = all libraries for Admin, Owner/Curator libraries for non-admins;
  Viewers get a read-only message. No writable libraries ⇒ read-only message.
- After a library is chosen, load state via existing `api.getApprovedItemIds(libraryId)`
  and `api.listHiddenItemIds(libraryId)` — no separate Calendar state cache.
- Mark target: movie SourceItem id or the representative SourceItem id for shows/anime.
  Hide target: movie SourceItem id or **all** `sourceItemIds` for a show/anime event.
  Use existing `api.approve` / `api.unapprove` and `api.hideItems` / `api.unhideItems`.
- Reuse `PosterGrid.tsx` labels and Sync warnings exactly (see §5); do not add a parallel
  hidden/approved state model, and keep `Sync applies to all libraries.` visible.
- On success: existing-style toasts, refresh approved/hidden ids for the chosen library,
  and refresh Calendar data. Keep the shared `BottomBar` unchanged.
- Re-run the full sandbox verification for this slice before promotion; ROADMAP items
  stay unchecked until then.

---

## 4. Backend detail

Raw JSON shapes to parse (already cached, no refetch):

- Movie: root `release_date` string.
- TV: root `next_episode_to_air` object with `air_date`, `season_number`, `episode_number`; `episode_number == 1` ⇒ `AirIsSeasonPremiere = true`.

`GET /api/calendar/events` response shape (actual slice-1 API, reconciled for slice 2):

```json
{
  "timeZone": "America/New_York",
  "today": "2026-11-18",
  "month": "2026-11",
  "events": [
    {
      "id": "…",
      "mediaType": "Show",
      "title": "Bluey",
      "showTitle": "Bluey",
      "date": "2026-11-21",
      "posterUrl": "…",
      "seasonNumber": 3,
      "episodeNumber": 1,
      "isSeasonPremiere": true,
      "tmdbId": 12345,
      "sourceItemIds": ["…representative first…", "…other episode ids…"],
      "representativeSourceItemId": "…"
    }
  ]
}
```

Note: the slice-1 API returns `id` / `showTitle` (not `sourceItemId` / `subtitle`) and no
`libraries` array. `sourceItemIds` / `representativeSourceItemId` are added in slice 2.
Per-library approved/hidden state is fetched through the existing per-library APIs from the
modal; it is never embedded in the Calendar response.

Dates are date-only strings, so server/browser timezone cannot shift an entry into the wrong cell. The returned `today` is computed with the configured `ScheduleTimeZone` singleton so the first render selects the server's current month.

Filtering rules:

- `mediaType` accepts `Movie`, `Show`, `Anime` (or omitted for all).
- `libraryId` omitted = union of libraries the caller can read; included = only that library, after membership check.
- Hidden-item state is read but not actionable in slice 1; calendar must not invent a new "hidden" concept. When slice 2 adds actions it will use the existing `HiddenItem` + Sync flow.

---

## 5. Frontend detail

- `CalendarPage.tsx` props (actual slice-1 API): `libraries`, `selectedLibraryId`, `onSelectLibrary`, `libraryMediaType`, `refreshKey`. The page fetches Calendar data itself; it does not receive `today`/`events`/`loading`/`error` as props. Keep it presentational where practical.
- Month state is a `YYYY-MM` string. Arrow buttons use UTC date math to avoid local-time drift. Heading uses an `<input type="month">` (with a `type="date"` fallback for older iOS Safari) hidden behind the label click.
- Grid uses CSS grid, 7 columns, mobile-safe wrapping/min-widths; this is not the separate responsive redesign item.
- Cells with multiple events show a compact stacked list; a "+n more" expansion may be added if needed for very busy days.
- All UI strings for Mark/Hide are deferred to slice 2; slice 1 cells and any detail popover are read-only.

---

## 6. Verification (sandbox first)

1. `docker compose build api web` (with `--no-cache web` only if a bundle looks stale).
2. Recreate sandbox from the new `:latest` images; wait for `http://localhost:5001/api/bootstrapped`.
3. Smoke checks; `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
4. Manual browser check at `http://localhost:5174`: log in with `admin@example.com` / `Passw0rd!123`; verify calendar nav no longer says coming soon, current month server-zone, arrows/date picker, filters, poster/title cells, New Season badge, and that Scan/Sync bottom bar still behaves as on the library page.
5. Confirm raw-JSON backfill populated dates in sandbox (sample movie + show with `next_episode_to_air`).
6. Record manual verification in `.project_documents/PROGRESS.md`.
7. Only after all of the above pass: `scripts/promote.sh` (or `--prod-only` if no code changes after verification). Hard-refresh and verify the served bundle hash differs from the current prod `assets/index-WyeMlDms.js` / `index-CoR6XILo.css`.

---

## 7. Risks / notes

- Raw TV responses fetched before this change did not request `next_episode_to_air` explicitly, but TMDB details responses include it by default; if a cached raw row lacks it, that show simply has no event (no live refetch).
- `episode_number == 1` is the current season-premiere heuristic; revisit if data shows inaccurate badges.
- Several episode rows share one show/TMDB id; calendar should de-duplicate show events by `(tmdbId, airDate)` so one upcoming episode is not shown once per locally stored episode.
- Admin/non-admin visibility must be checked against the existing library membership code, not a new permission table.
