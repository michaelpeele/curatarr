# Curatarr — Roadmap

> **LANGUAGE:** English preferred.

## Bugs

- [x] **1. Invite accept leaves the user stranded** — **Done 2026-11-18.** The accept page now shows
      a success toast containing the login email, attempts auto-login, and navigates to `/` on both
      success and failure; the endpoint returns `email` in its response. Verified by
      `INVITE_E2E=1` (accept, fresh login, `/api/me` Viewer, replay 400, cleanup) and Playwright in
      an isolated browser context. Originally described as: when an invited user opens the invite
      link and completes the form (display name + password), nothing happens: no redirect and no
      sign-in screen. Expected: after a successful accept they land on the sign-in screen (email
      prefilled if possible) or are signed straight in.
      Repro: invite a user → open invite link → fill name + password → submit.
      - **Analysis / implementation plan:** `.project_documents/PLAN-sprint5-multiselect-match-invite.md`
        (shipped with Sprint 5 #4 and #5).
- [x] **2. Oversized posters after fast scrolling** — **Fixed 2026-11-18.** Confirmed by the user
      on mobile (3 posters/row): after scrolling fast to *A Beautiful Mind*, the rightmost poster
      *A Better Tomorrow* blew up in size. Root cause: the mobile grid used `repeat(3, 1fr)`;
      plain `1fr` is `minmax(auto, 1fr)`, so a loaded poster's intrinsic min-content width could
      expand the rightmost track when pages appended during a fast flick. Fix in
      `PosterGrid.tsx`: mobile grid → `repeat(3, minmax(0, 1fr))`; `LazyMount` wrapper and the
      PosterCard/ShowCard roots got `minWidth: 0, width: "100%"` so skeleton and mounted states
      reserve the same width. Verified at 390x844 with injected posters through two rapid
      multi-page flicks (34→94→132 cards): tracks stayed ~94.7px, every card 94.7px, no oversize,
      no horizontal overflow, 0 page errors. Promoted with `promote.sh --prod-only`, prod bundle
      `assets/index-B34SAEQX.js`. Previously reported 2026-11-17 as a desktop repro, which could
      not be reproduced there (57,602 rAF samples, max = column width).
- [x] **3. Hide/unhide + view-filter switching blanks the grid** — **Fixed 2026-11-17.** Reported
      after the S5 #9 rollout: Hide removes a card immediately; `Hidden <Type>` shows it; unhiding
      there removes it immediately; then opening the filter drop-down made the whole filter bar
      disappear and left only `No titles match these filters`. Root cause: opening a dropdown calls
      `ensureDetails()`; in paged mode it always refetched page 0 with the *currently selected*
      view, so after the optimistic unhide it replaced the page with an empty Hidden result, and the
      zero-result early return unmounted the toolbar. Fix in `PosterGrid.tsx`:
      `ensureDetailsPaged()` now refetches page 0 only when the detail backfill actually changed
      rows (otherwise it just reloads facets), and zero-result rendering moved below the toolbar
      with the empty state derived from the visible groups, so every filter combination stays
      recoverable. Verified in sandbox (`assets/index-BfJ29BrT.js`): hide → Hidden → unhide →
      reopen dropdown kept the filter bar and the `All Movies` option; choosing All restored 1,877
      titles; no console errors; hidden test data cleaned; promoted with `promote.sh --prod-only`.
- [x] **4. Hide is applied immediately instead of being batched with Sync** — **Fixed 2026-11-17.**
      Previously clicking **Hide** removed the card from `All <Type>` immediately and unhiding in
      `Hidden <Type>` restored it immediately; Sync never touched hidden symlinks. Hide/unhide are
      now two-phase requests with the same boundary as Mark: gray → **yellow pending** on click,
      green / actually moved only after **Sync**. Added `HiddenItem.UnhideRequestedAt`
      (migration `20261117191901_AddHiddenItemUnhideRequests`) and the pure `HiddenItemState`
      helpers. `/api/source-items?libraryId=` and `/api/library-entries` now emit *effective* hidden
      state from the request stamps against the library's `LastReconciledAt`; pending hides stay
      visible with a yellow eye, pending unhides stay in `Hidden <Type>`. Reconcile skips hidden
      approvals (so their stale links are removed), then clears completed unhide requests so the
      same Sync also restores their links. Verified in sandbox end-to-end: hide → yellow eye/card
      stays → Sync → green eye/card leaves All and total 1,877→1,876 → unhide → yellow eye/card
      stays in Hidden → Sync → hidden-items `[]`, total back to 1,877, gray eyes. 64/64 tests green;
      no console errors; promoted with `promote.sh --prod-only`, prod bundle `assets/index-C6WaqyNp.js`.
- [x] **5. Grid does not refresh after Sync — hide leaves card until a filter toggle** — **Fixed
      2026-11-17.** Follow-up regression exposed by Bug #4: hiding a title (yellow, card stays) then
      clicking **Sync** left the card in `All <Type>`; it only disappeared after toggling the view
      filter to Hidden and back. Root cause: `handleReconcile()` in `App.tsx` never bumped
      `posterRefreshKey`, so `PosterGrid` never refetched page 0 / annotations after Sync; the local
      optimistic `hidden=false` persisted until a filter toggle changed the fetch callback identity.
      The server was already correct (effective hidden derived from its own `LastReconciledAt`).
      Fix: `handleReconcile()` now bumps `posterRefreshKey` (forces page-0 refetch) and
      `librariesVersion` (reloads libraries so `lastReconciledAt` updates and the eye turns green).
      Verified in sandbox by clicking the UI **Sync** button with no reload or filter toggle: the
      yellow "Hide requested" card vanished immediately, total 1,877→1,876, green eye in Hidden,
      unhide → Sync restored to 1,877; 0 console errors. Promoted with `promote.sh --prod-only`, prod
      bundle `assets/index-Bo0SBBNN.js`.
- [ ] **6. Multiselect bulk controls scroll out of view after Select all** — reported 2026-11-19
      after the recent library-grid UI update. Repro: enter Multiselect, click **Select all**, then
      scroll the grid; the bulk options (selected count, Select all/Clear, Mark/Hide selects, Cancel,
      Apply) disappear after the first scroll. Expected: while Multiselect is active, the bulk bar
      stays frozen/sticky and the selected actions remain visible. Proposed fix: keep the bulk bar
      pinned below the sticky filter toolbar with an opaque background, border and explicit z-index;
      do not change selection semantics, and verify it does not overlap `BottomBar` on mobile/iOS.
      This is a separate UI follow-up; do not fold it into Group D registry/multi-arch CI work.

## Immediate (after smoke-test sign-off)

- [x] **Re-invite bug fixes** — orphaned Identity rows cause 400 on re-invite.
      Fix: logging + `ChangeTracker.Clear` + orphan-check before `userManager.CreateAsync`.
      **Done 2026-11-14:** pending-invite rows (`EmailConfirmed=false`, no password) are
      detected via `FindByEmailAsync` and get a fresh link resent (200) instead of a
      duplicate-row 400; active accounts get a friendly 400; role/email failures roll the
      new row back; `ILogger` structured logging on all paths; `test-api.sh` steps 7–9.
- [x] **Real EF Core migrations** — **Done 2026-11-15:** `InitialCreate` generated from the
      model into `Curatarr.Engine/Data/Migrations` (includes the people columns, `TmdbRawDetails`,
      `PathMappings`, and both unique indexes now declared in `OnModelCreating`). Startup uses
      `MigrateAsync`; pre-migration DBs are auto-baselined (history row inserted when
      `SourceItems` already exists) so nothing is replayed. Verified: prod baselined in place
      (82,490 items / 8 approvals intact, integrity ok), sandbox clone baselined, fresh DB
      created from the migration passed the full `test-api.sh`, `has-pending-model-changes`
      reports none.
- [x] **API tests for wizard** — add `bootstrap` / `isBootstrapped` coverage to `test-api.sh`
      **Done 2026-11-14:** section 0 (state check + `WIZARD_ONLY=1` happy path:
      bootstrap → isBootstrapped=true → login → Admin role) and step 10 (one-shot guard
      400 after users exist). `BASE=` env targets other instances (sandbox port 5001).
- [x] **Sandbox / staging stack** — `docker-compose.sandbox.yml`: isolated api+web
      (project `curatarr-sandbox`, ports 5001/5174, own volume + network, no Tailscale,
      console email, isolated `/output`) to test new builds before promoting to prod;
      seedable via **`scripts/sandbox-seed.sh`** (2026-11-15) — stops the stack, copies
      the prod DB (db+wal+shm) as the base, verifies integrity, salvages the sandbox
      volume's SourceItem detail columns row-by-row (tolerates WAL corruption from live
      copies), fixes ownership to uid 1654 (the api image's non-root user), and
      optionally nulls N random `Rating` rows (`--null-ratings N`) to exercise the
      filter-status UI. `--no-salvage` for a fresh prod-clone. See file header for the
      full workflow.

## Short-term

- [x] **Studio/Network filter** — filter by release company (movies) or broadcast network (TV):
      - **Movies:** Production company filter (e.g., A24, Warner Bros, Disney) → `production_companies` from TMDB `/movie/{id}`
      - **TV Shows:** Network/broadcaster filter (e.g., AMC, ABC, HBO) → `networks` from TMDB `/tv/{id}`
      - Dropdown populated from distinct values across scanned items (not a fixed list)
      - Auto-switches based on library media type (same pattern as Rating filter)
      - Requires new columns on SourceItem (`ProductionCompany` for movies, `Network` for shows) + backfill via existing `GetMovieGenreIdsAsync`/`GetShowGenreIdsAsync` pattern (rename to a combined "fetch details" call to avoid extra TMDB requests)
      **Done 2026-11-14:** 3 new `SourceItem` columns (`Rating`, `ProductionCompanies`,
      `Networks`), added to prod DB via raw `ALTER TABLE` (no EF migration — `EnsureCreated`
      covers fresh DBs). `GetMovieGenreIdsAsync`/`GetShowGenreIdsAsync` replaced by a single
      `GetCatalogDetailsAsync(tmdbId, mediaType)` (details + appended `release_dates`/
      `content_ratings` in one TMDB round-trip). Shared `DetailsBackfill` helper drives both
      the `/api/backfill-genres` endpoint (now returns `pending`) and the renamed hourly
      `DetailsBackfillService` (progress guard: breaks a run when a batch yields zero
      changes). Full catalog backfilled: **38,212/38,212, 0 failed**. UI: generic
      `SelectFilter` dropdowns for genre/rating/studio/network, distinct-value options
      computed from rendered entries, `ensureDetails` fires on dropdown open (60s foreground
      cap → background job), studio/network filters narrow to their own media kind, filters
      reset on library media-type switch. Verified in-browser (Playwright): Movie library
      shows Studios (not Networks), TV library shows Networks (not Studios); PG-13 → 409
      movies, Walt Disney Pictures → 30 movies, Nickelodeon → 11 shows.
      **Status UI added 2026-11-15:** filter buttons never hijack their label to
      "Loading details..." — they always show the selection (or "All X") + arrow.
      Inline status text after the dropdowns shows `Loading... x / y` while the
      foreground backfill runs (total computed locally, completed = total −
      `resp.pending` per batch), `✓ Completed` for 10s then auto-hide; 60s cap →
      `x / y — finishing in background` (10s); TMDB-stuck → `x / y — no progress,
      will retry in background` (10s). Redundant success/info toasts removed. No
      status at all when pending=0 (prod today).
- [x] **Rating filter** — add rating-based filtering under a "Rating" dropdown:
      - **Movies:** Filter by movie rating (G, PG, PG-13, R, NC-17) when Movie library selected
      - **TV Shows:** Filter by TV rating (TV-Y, TV-Y7, TV-G, TV-PG, TV-14, TV-MA) when Show library selected
      - Dropdown automatically switches based on selected library's media type
      - Requires TMDB rating data in SourceItem (new column + backfill like genres)
      **Done 2026-11-14:** `Rating` column (US certification) + canonical ordering
      (movie certs first, then TV ratings, unknowns appended alphabetically). See the
      Studio/Network note above for the shared implementation and verification.
- [x] **Part A: People fields + raw TMDB staging table** — store searchable
      director/creators/cast now (they weren't stored; the detail modal fetched them
      live on click only):
      - **Columns:** `Director` (movies), `Creators` (shows/anime), `Cast` (both,
      top 30 in billing order) on `SourceItem` — same null/"" sentinel semantics as
      the other detail columns; columns ride the existing `DetailsBackfill` machinery
      (hourly service + dropdown-open foreground pass) and the status text.
      - **`TmdbRawDetails` staging table:** full raw TMDB response per unique
      `(mediaType, tmdbId)` (2,460 titles, ~200 MB) stashed during the same
      round-trip — future field expansions (budget, tagline, runtime, writers...)
      parse locally instead of refetching; `RequestedWith` tracks which
      append_to_response set was used so only stale keys get a targeted refetch.
      - **Done 2026-11-15:** provider fetches `append_to_response=…,credits`
      (still one TMDB call/title), backfill writes columns + atomic raw-JSON upsert,
      DTO/client/showGroups/needsDetails expose everything; prod migrated in one
      API-stopped window (3 ALTERs + CREATE TABLE).
      **Sprint 2 Part B** (free-text search over title + director/creators + cast,
      token-AND with implicit wildcards, input between the dropdowns and Clear
      filters) builds on this — **done 2026-11-15** (see Sprint 2).
- [x] **Timestamps in logs** — **Done 2026-11-15:** every API log line is prefixed with an
      ISO 8601 timestamp + UTC offset (`2026-11-15T12:33:30.918+00:00`) via the simple console
      formatter; startup diagnostics (TMDB key, email mode) use the same `LogStartup` prefix.
- [x] **Show-level metadata** — **Closed 2026-11-19 as superseded by Calendar slice 1.** The
      user's goal was to bubble up the next-episode air date in the Calendar, not episode
      drill-down. `SourceItem` now has explicit release/air-date columns backfilled from cached
      `TmdbRawDetails.Json`, and the Calendar renders next episodes; see
      `.project_documents/PLAN-calendar-from-metadata.md` and
      `.project_documents/PLAN-metadata-show-enrichment.md`. Per-show endpoints, last-updated
      fields, and episode drill-down remain retired. Original spec retained for reference:
      add air-date / last-updated fields to `SourceItem`; show cards currently show episode
      count, season count, and latest `SxxEyy`; per-card refresh re-fetches the whole catalog
      because there is no per-show endpoint; a real show entity would enable episode drill-down.

- [ ] **Atomic Design frontend restructure** — migrate flat `components/` (16 .tsx files) into
      atoms → molecules → organisms hierarchy with shared `theme/`, `hooks/`, `api/` dirs.
      Use MetaDatarr as pilot template (smallest frontend). See MetaDatarr `code/frontend/.project_documents/MIGRATION_PLAN-atomic_design.md` for reference approach.
- [ ] **Docker image & container cleanup / consolidation:**
      - Explicit image tagging (`curatarr-api:${CURATARR_VERSION:-latest}`, `curatarr-web:${CURATARR_VERSION:-latest}`) to eliminate compose prefix repetition (`curatarr-curatarr-*`). **DONE 2026-11-19** as part of D1; the single-container option below remains open, so this whole item stays unchecked.
      - Single-container deployment option: consolidate ASP.NET Core API and React static SPA (`wwwroot` hosting + proxy removal) into a single standalone container image for lightweight setups.
- [x] **Mobile & Tablet Responsive Views** — **Done 2026-11-19 (narrowed by user decision).**
      Both mobile and tablet poster-column changes are RETIRED: the existing 3-column mobile grid
      and `repeat(auto-fill, minmax(150px, 1fr))` tablet grid are unchanged. Delivered via the
      shared `useViewport()` hook, full-screen touch modals, 44px touch targets, and a compact
      tablet sidebar; sandbox-verified and promoted with `index-R4dyCEyO.js` / `index-fENfh4zq.css`.
      Original spec retained for reference:
      - **Mobile (320px–576px):** 1–2 poster columns, bottom navigation (`BottomBar.tsx`), full-screen touch modals, zero poster clipping.
      - **Tablet / iPad (768px–1024px):** 2–4 adaptive poster columns, compact drawer/sidebar, touch padding.
- [x] **TMDB metadata enrichment polish** — **Closed 2026-11-19 after re-scope.** Posters,
      genres, ratings, studios/networks, and people fields are already shipped; the remaining
      unshipped item was backdrops (large banner images), which the user confirmed are not
      needed. See `.project_documents/PLAN-metadata-show-enrichment.md`.
- [x] **Calendar view** — **Duplicate of Sprint 4 #2 "Calendar from metadata"; completed 2026-11-18.**
      This short-term entry was a pre-implementation placeholder spec. The real work is tracked and
      done under Sprint 4 #2 — Calendar slice 1 and slice 2 were both sandbox-verified and promoted
      to prod. Original spec retained for reference:
      - Filter by library (show only releases for items in a specific Virtual Library)
      - Filter by type (Movie / Show / Anime) across all libraries
      - "All" view: click a release → modal to link it to an existing library
- [x] **Activity / logs view** — **Duplicate of Sprint 5 #1 (Activity) and #2 (Logs); completed
      2026-11-18.** Those items were sandbox-verified and promoted to prod. This short-term
      placeholder is closed here; no separate implementation exists or is needed.
- [ ] **Library suggestions** — "add to library" suggestions from unwatched metadata

## Sprint 2

- [x] **Free-text search filter (Part B)** — text input between the Studio/Network
      dropdown and Clear filters; token-AND with implicit wildcards: lowercase + split
      on whitespace, every token must case-insensitively substring-match the row's
      haystack (movie → title + director + cast; show → showTitle + creators + cast;
      overview excluded). `charlie` → all Charlie Brown titles; `charlize theron` →
      matches in either order. ANDed with active dropdown filters; Clear filters
      empties it; typing fires the existing `ensureDetails()` so the status text
      covers the backfill interplay. No debounce needed (client-side filtering).
      **Done 2026-11-15:** haystack map memoized and built only while a search is
      active; search-only input makes Clear filters appear; reset on library switch;
      empty-state line names the query. Verified on prod (Nolan → 14, "christopher
      dark" → 3 Dark titles + Dark Shadows, overview-only word → empty state) and
      shows (vince + Comedy → 4).
- [x] **Filter bar stays on screen with a hide toggle** — the Library-tab filter
      toolbar (dropdowns + search + status) is sticky at the top of the scroll area,
      visible by default, with `Hide filters` (collapses to a `Show filters (n
      active)` button + status text) and `Show filters` to restore. Collapsing closes
      any open dropdown but keeps selections/search applied.
      **Done 2026-11-15.**
- [x] **Session persistence & 401 handling** — **Done 2026-11-15.** Data Protection keys
      now persist in the `/app/data` volume (container recreates no longer invalidate
      cookies), the auth cookie is a 30-day persistent sliding `curatarr.auth`, and any
      401 from a non-auth endpoint drops the SPA to the login screen with
      "Your session expired. Please sign in again." instead of raw error toasts. Sandbox
      uses its own cookie name (`Cookie__Name`) so localhost prod/sandbox sessions don't
      clobber each other. Verified: same cookie survived forced API recreates (and a full
      rebuild); cleared cookies mid-session produced the login screen + notice.

## Sprint 3

- [x] **Scheduling / Dynamic Availability Engine (Slice 1 — model + API + worker, no UI)** — **Done 2026-11-15.**
      - `Curatarr.Engine/Data/Models.cs` adds `LibrarySchedule` (Id / VirtualLibraryId FK cascade-delete / Name / Enabled / DaysOfWeek CSV int / StartTime & EndTime TimeOnly nullable / optional StartMonth/StartDay/EndMonth/EndDay for seasonal / CreatedAt/UpdatedAt).
      - `Curatarr.Engine/ScheduleEngine.cs` — pure static helper: `IsActive` (union of enabled rules, day+time+optional season check, empty set = Always On), `NextTransition` (minute-resolution candidate sweep up to 366 days), `Validate`, `NormalizeDays`.
      - `ReconcileGate` (`SemaphoreSlim(1,1)`) serializes `/api/reconcile` and scheduled evaluations so they never overlap; `SymlinkEngine.ReconcileLibraryAsync(libraryId, active)` reuses existing `ReconcileCoreAsync`.
      - `LibraryScheduleService` hosted service — 15s startup delay then every 60 s; flips library when state changes, logs transition.
      - `Curatarr.Api/ScheduleEndpoints.cs` — admin-only CRUD for schedules, `GET .../schedules/status?at=<ISO>` (deterministic test hook), `POST .../schedules/evaluate` (fire reconcile now).
      - Timezone read from `appsettings.json` → `Schedules:TimeZone` (default `""` → server local); added to compose env.
      - EF Core migration `20261115141233_AddLibrarySchedulesTable` applied to prod baseline (no data loss, 0 schedule rows).
      - 47 unit tests covering always-on default, single-rule weekday/time, overnight cross-midnight, seasonal wrap-across-year, multi-rule union, disabled ignored, invalid-payload rejection, deterministic `?at=` override, gate serialization, null-field handling — all green.
      - Browser/API smoke via sandbox: create → get → update → status with `?at=` across boundaries → evaluate → delete round-trip verified.
- [x] **Scheduling Slice 2 — Settings → Server timezone picker (UI)** — **Done 2026-11-16.**
      - `Curatarr.Web/src/components/ServerPage.tsx` — admin-only Settings → Server view with a
        schedule timezone dropdown listing `TimeZoneInfo.GetSystemTimeZones()`; shows the current
        zone, the server's current time in that zone, and whether it is configured.
      - API `GET/PUT /api/schedules/settings` (`Curatarr.Api/ScheduleEndpoints.cs`) validates the
        zone against the system list and persists it to `schedules-config.json` in the content root
        (loaded at startup in `Program.cs`). `LogTimeZone` singleton is reloaded on PUT.
      - Sidebar `settings-server` entry + `App.tsx` route (admin-only; non-admins are redirected).
      - Deployed and verified on prod (zone now `America/New_York`). The schedule engine reads the
        zone at startup, so an API restart is required after changing it.
      - Custom `ZoneConsoleFormatter` was abandoned (incompatible with the .NET 10 runtime); logging
        is plain `AddSimpleConsole` with `TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz "`, i.e. log
        timestamps remain UTC.
- [x] **Scheduling Slice 3 — Schedule modal + library status text (UI)** — **Done 2026-11-16.**
      - `Curatarr.Web/src/components/ScheduleModal.tsx` (~430 lines) exports
        `ScheduleModal({libraryId, libraryName, onClose})` and `formatStatus(st)`.
      - First page is a two-button chooser: **Daily** or **Seasonal** — no pickers until one is
        chosen. Each mode page has a `Back` button, and Cancel/Close + Save appear only after a
        mode is selected.
      - Daily = 7 weekday rows (checkbox + day label + custom `TimeRangeField`). `TimeRangeField`
        is Safari-safe (Safari renders no picker UI for `<input type="time">`): a text box showing
        `6:30 PM – 8:00 PM` (or `pick a time`) opens a popover with Hour/Minute selects, an AM/PM
        toggle showing the current meridiem, and `Next`/`Done`. Committing the start auto-advances
        to the end picker pre-filled start + 2 h. A row's checkbox ticks itself once both times are
        set (blank/equal times still mean whole day `00:00`–`00:00` on save). One rule per checked
        day named `"{DAY} window"` with `daysOfWeek: [day]`.
      - Seasonal = native `<input type="date">` from/to pairs with `+`/`−` row controls (remove
        disabled at one row). The display year is decorative (current year; Feb 29 → next leap
        year) because only month/day is stored, and the caption says so: `Repeat every year — only
        the month and day are saved, so the year in the date picker is ignored.` Rows missing any
        of the four date fields are discarded on save; rule is `daysOfWeek: []` (all days),
        `00:00`/`00:00`, named `"Season M/D–M/D"`.
      - Existing rules are hydrated back into the form; Save diffs form vs server (create/update/
        delete) and deletes all rules belonging to the other mode.
      - Caption shows `Times in {tz}.` from `getScheduleSettings`; status line refreshes every 60 s
        while open.
      - `client.ts` adds `LibrarySchedule` + `ScheduleStatus` types and
        `listSchedules/createSchedule/updateSchedule/deleteSchedule/getScheduleStatus/evaluateSchedule`.
      - `BottomBar.tsx` adds a `Schedule` button immediately RIGHT of `Sync` (disabled when no
        library is selected) plus a status `<span>` (`Stops showing {when}` when active,
        `Shows next {when}` when inactive, `Always on` when fail-open, empty when no rules),
        polled every 60 s for the selected library.
      - Verified: `docker compose build web` green; served bundle contains the new strings
        (`assets/index-CnXk84hj.js`, later `assets/index-BHn5wMW3.js` after the redesign). Sandbox
        API round-trip (daily rule → season rule → evaluate → delete → back to always-on) all green,
        and `test-schedules.sh` against sandbox: 27 passed / 0 failed. Sandbox web (`:5174`, same
        `curatarr-web:latest` image) serves the current bundle.
- [x] **Plex OAuth + Refresh Trigger** — **Done 2026-11-16.** Integrate with Plex so library changes are pushed to Plex immediately:
      - **Plex OAuth:** PIN-based flow against `https://plex.tv/api/v2/pins?strong=true`; `POST /api/plex/oauth/start` returns the `app.plex.tv/auth` URL, `GET /api/plex/oauth/poll/{pinId}` exchanges the PIN for a token, then fetches `/api/v2/user` + `/api/v2/resources` for account/server names.
      - **Store Token:** singleton `PlexSettings` entity (EF migration `20261116015100_AddPlexSettings`); token encrypted with ASP.NET Data Protection (`IDataProtector("Curatarr.Plex.Token")`) and never returned by the API. `GET /api/plex/status` reports linked state, account/server, server URL, refresh toggle, last refresh.
      - **Trigger Refresh:** `PlexService.TriggerLibraryRefreshAsync` maps the virtual library `OutputPath` through the existing `PathMapping` rows (longest prefix, `/`-normalized, case-insensitive), matches the Plex `/library/sections` location, then `POST /library/sections/{key}/refresh`. Fire-and-forget; failures are logged and recorded on `PlexSettings`, never surfaced to the caller.
      - **Hooks:** full reconcile (`POST /api/reconcile`, i.e. the **Sync** button), library output-path change, manual schedule evaluate, and every schedule flip from the worker. Approve/unapprove deliberately do **not** hook in — mark/unmark only records intent, and a single Sync then materializes every symlink and refreshes Plex once for the whole batch.
      - **UI:** Settings → Plex page (`PlexPage.tsx`, admin-only) with Link Plex popup + PIN polling, linked account/server, editable server URL override, "Refresh Plex after changes" toggle, Unlink, last-refresh status. Settings nav now has a gear icon and Server a stacked-server icon.
      - **Purpose:** content appears in Plex immediately after approvals/schedule flips, instead of waiting for Plex's periodic scan.
- [x] **Plex auto-refresh follow-up fixes** — **Done 2026-11-17.** Three defects found while diagnosing
      "I added a movie and Plex did not update":
      - **Approve did not trigger Plex** — `POST /api/virtual-libraries/{id}/approve` and `.../unapprove`
        only wrote the DB row, so a newly marked title sat invisible until the next Sync. An intermediate
        version made each mark reconcile + refresh Plex inline, but that fired a full Plex scan on every
        click; the final design is **batch semantics** — mark/unmark records intent only, and **Sync**
        (`POST /api/reconcile`) does the symlink reconcile and the Plex refresh once per batch.
      - **Unapprove left the symlink behind** — `SymlinkEngine.ReconcileCoreAsync` scoped stale-record
        cleanup by current `VirtualLibraryItem` ids, so a record whose item had just been unapproved was
        invisible and never deleted. Now also scoped by the library's `OutputPath`, so the link (and its
        empty folder) is pruned by the next Sync.
      - **Path matching** — the `/output` → host mapping had to become
        `/output` → `/Volumes/Macintosh HD/Volumes/data`, because Plex reports those sections under the
        alternate volume root; `SectionContains` also gained a path-boundary suffix match for that case.
      - **Verified on prod:** mark a movie → nothing on disk and `lastRefreshAt` unchanged; press **Sync**
        → the symlink appears under `/output/VirtualLibraries/peeles_kids_movies` and
        `GET /api/plex/status` moves to `Refreshed 2 section(s)`; unmark then Sync → link and empty folder
        removed. Library returned to its 4 approved items afterwards.

## Sprint 4

- [x] **Bottom-bar button labels** — **Done 2026-11-16.** Renamed `Sync files now` → `Sync`
      and `Scan sources` → `Scan`. Follow-up: the change appeared missing until the web image was
      rebuilt with `docker compose build --no-cache web` (Vite bundle caching) and the browser was
      hard-refreshed (Ctrl+Shift+R).
- [x] **Access-control hardening** — **Done 2026-11-16.** Backend audited: every
      configuration surface is Admin-gated (`/api/source-roots` GET/POST/PUT/DELETE,
      `/api/path-mappings`, `/api/filesystem/browse`, `/api/virtual-libraries`
      list/create/edit/delete + members, `/api/users`, `/api/scan*`, `/api/reconcile`,
      `/api/backfill-genres`, `/api/schedules/settings`). Content approval is
      membership-gated per library: Admins bypass, Owners/Curators need a membership
      row on THAT library, Viewers are rejected (`approve`/`unapprove`). Frontend:
      new guard in `App.tsx` — any view other than `library`/`account`/`calendar`
      resets to the library view for non-admins (covers demotion mid-session and
      stale view state; the Sidebar already hides admin entries). Verified on
      sandbox with a Viewer (403 on all config + scan/reconcile/browse) and a
      Curator (approve own lib 200, approve other lib 403, list-all 403).
- [x] **Library selector label** — **Done 2026-11-16.** Added a `Library:` prefix label before the
      bottom-bar dropdown (`BottomBar.tsx`); deploy verified in served bundle
      `assets/index-BYqDsGgu.js`.
- [x] **Uniqueness validation** — **Done 2026-11-16.** `Curatarr.Api/Program.cs` now rejects
      duplicates with `409 { error }` on `POST/PUT /api/source-roots` (case-insensitive display
      name → "A source with that name already exists — choose a different name."; duplicate path →
      "A source with that path already exists.") and on `POST/PUT /api/virtual-libraries` (duplicate
      `OutputPath` → "Another library already uses that output path."). Names/paths are trimmed and
      paths normalized (backslashes → `/`, trailing separators dropped) via a `NormalizePathKey`
      helper, and PUT excludes the record being edited. Verified on prod: duplicate name `MOVIES`
      and path `/media/movies/` → 409, duplicate library output path with trailing slash → 409,
      valid create/self-update → 200.
- [x] **Media-type view filter + hide-per-library** — **Done 2026-11-17.** New first filter
      (before Genres):
      `All <Type>`, `Library <Type>`, `Unmarked <Type>`, `Hidden <Type>`.
      **Confirmed semantics (2026-11-15):** default = `All <Type>` = whole catalog minus hidden;
      `Library <Type>` = approved in the selected library minus hidden; `Unmarked <Type>` = not
      approved minus hidden; `Hidden <Type>` = hidden items only. Hidden state is stored per
      library (new table) and only affects that library; add a `Hide from Library` button under
      `Mark for library`.
      - **Implementation reference:** `.project_documents/RESEARCH-batch1-filter-and-icons.md`
        (endpoint contract, filter semantics, icon state derivation, file-by-file plan).
      - **Shipped:** `client.ts` (`listSourceItems(libraryId)`, hide/unhide/list hidden-item
        methods, `Approved`/`ApprovedAt`/`Hidden` flags, `LastReconciledAt`), `App.tsx`, and
        `PosterGrid.tsx` (view filter, pinned `CardActions`, per-library hide with optimistic
        rollback + toast). Verified on sandbox :5174: all four filter options, Hide → 200 and
        item leaves All view, Hidden view shows red-circle-slash eyes and unhide toggle. Bundle
        `assets/index-QTnbk16h.js`; promoted to prod via `promote.sh --prod-only`.
- [x] **1. Wizard media-type once** — **Done 2026-11-18.** The first-boot wizard now asks for one
      media type in step 1; the per-source-root and per-library selects were removed. The single
      choice is sent as top-level `mediaType`, parsed once by the API (400 on invalid values), and
      applied to every bootstrapped source root and the first library. Legacy payload fields are
      still tolerated. `test-api.sh` payloads were updated; a fresh-DB wizard happy path, the
      one-shot guard and a 400 for an invalid type all passed in a throwaway API container.
      - **Analysis / implementation plan:** `.project_documents/PLAN-sprint4-wizard-and-scoped-scan.md`
        (shared batch with Sprint 5 #3). Promoted with `promote.sh --prod-only`; prod bundle
        `assets/index-DnVNfPJu.js`.
- [x] **2. Calendar from metadata** — **Done 2026-11-18.** New Calendar view driven by cached
      TMDB release/air dates (no live TMDB calls):
      - Month grid defaulting to the current month (server `Schedules:TimeZone`); `‹` / `›` arrows
        step months, and clicking the "Month YYYY" heading opens a date picker to jump to any month.
      - Cells list movie release dates and show air dates; entries show poster + title and a
        `New Season (#)` badge for season premieres.
      - In All libraries view, clicking an entry opens `CalendarItemModal` with a library picker
        plus `Mark for library` / `Visible in this library — click to hide` buttons, matching the
        main library page wording and Sync warnings exactly. Concrete-library entries stay read-only.
        Shared BottomBar/Scan/Sync behaviour preserved (`Sync applies to all libraries.`).
      - **Sequencing:** reuses the per-library hide + media-type view filter states
        (Sprint 4 #1, Done 2026-11-17).
      - **Plan / slice split (2026-11-18):** `.project_documents/PLAN-calendar-from-metadata.md`.
        Slice 1 (read-only foundation) promoted at 15:09Z; slice 2 (All-view link/Hide modal)
        sandbox-verified and promoted at 18:00Z. Prod bundle `assets/index-C4TfM9WN.js`,
        CSS `index-CoR6XILo.css` (unchanged).
- [x] **Plex client refresh parity (path-scoped scan)** — **Done 2026-11-17.** After a Sync the Plex
      *server* scanned but new titles only appeared in Plex **clients** after navigating away and
      back. Root cause found in Sonarr/Radarr source (`PlexServerProxy.Update`): both call
      `GET /library/sections/{id}/refresh?path={absolute item folder}&X-Plex-Token=…`, a **partial
      scan scoped to the changed folder**, while Curatarr posted a bare `POST
      /library/sections/{key}/refresh` — a full-section rescan that queues behind whatever Plex is
      already doing. `PlexService.TriggerInBackground`/`TriggerLibraryRefreshAsync` now take an
      optional `changedPaths` collection; `POST /api/reconcile` derives the item folders from
      `ReconcileReport.Created`/`Removed`, translates them through `PathMapping`, and issues one
      path-scoped `GET …/refresh?path=…` per (section, folder) via the new `ResolveSectionPath`
      helper. Falls back to the old full-section refresh when no section covers the changed folder.
      Status/log now report `Refreshed N path(s), M section(s) (path-scoped)`.
      **Removal follow-up (same day):** path-scoped scans cannot convey a *deletion* — Plex ignores a
      `path` that no longer exists — so `ReconcileReport.Removed` folders now force a full section
      rescan with `force=1` (and additions also send `force=1` so Plex re-reads instead of trusting
      cached timestamps). `TriggerInBackground`/`TriggerLibraryRefreshAsync` take separate
      `changedPaths`/`removedPaths`. Helpers: `RefreshPathAsync`, `RefreshSectionAsync`,
      `SendRefreshAsync`.
      Verified on prod: approve `12 Days of Christmas Eve (2004)` + Sync → `Refreshed 1 path(s)`;
      unapprove + Sync → folder pruned. Garfield: Sync → in Plex section 13 with `addedAt` 2 s later;
      unapprove + Sync → dropped from Plex; re-approve + Sync → back, `addedAt` new. Diagnostics
      `GET /api/plex/sections` and `GET /api/plex/sections/{key}/recent` used for all of this.
      **Apple TV follow-up:** desktop Plex updated instantly but tvOS lagged. Sonarr/Radarr add
      nothing beyond the same scan call, so the gap was the server preference
      `FSEventLibraryUpdatesEnabled` (Scan my library automatically) being **off** — Plex only ever
      saw changes when a tool nudged it, and tvOS caches Home hubs hardest. Added Admin
      `GET /api/plex/prefs` and `PUT /api/plex/prefs` and enabled auto-scan on the server (verified
      `True`). Scheduled scans remain off. Hardlinks are not viable: `/media` and `/output` are
      separate bind mounts inside the container (`Invalid cross-device link`).

## Sprint 5

- [x] **1. Wiring up Activity** — **Done 2026-11-17.** Settings → Activity is a live, Admin-only
      audit view backed by persisted `ActivityEvent` rows. See `.project_documents/PROGRESS.md`.
      Originally described as: fill in the Settings → Activity page with a live list of
      activities generated by the app:
      - **Scheduling** — schedule created/edited/deleted, library showing/stopped flips.
      - **User** — logins, invites, role/membership changes, password resets.
      - **Plex** — link/unlink, refresh requests and their results/failures.
      - **Scan** — scan started/finished/stopped, with a countdown timer while a scan is running.
      - **Source** — source root added/edited/removed.
      - **Path** — path mapping changes, library output path changes and reconcile results.
      - Entries persisted with timestamp, actor, type and detail; newest first, paged.
      - **Implementation plan:** `.project_documents/PLAN-sprint5-activity-and-logs.md` (shared
        Activity + Logs batch; do not start until approved).
- [x] **2. Wiring up Logs** — **Done 2026-11-17.** Settings → Logs is a live, Admin-only viewer
      with Serilog rolling files, config persistence and a SignalR stream. See
      `.project_documents/PROGRESS.md`. Originally described as: turn Settings → Logs (currently a
      placeholder page) into a live log viewer:
      - Show the current log output with a **Refresh** button and a **Monitor live** checkbox that
        streams new entries while enabled.
      - Log-level dropdown (Trace/Debug/Information/Warning/Error/Critical) that changes the
        running log level immediately. Selecting **Debug** shows a warning that it produces a large
        volume of logs and may affect performance — troubleshooting only.
      - File logging with retention: rotate/zip log files once they reach a size threshold, and
        delete archives older than the retention age. Default retention **7 days**.
      - Retention configurable from the Logs page (and its underlying JSON config) up to **90
        days**, with picker options **7 / 14 / 21 / 30 / 60 / 90**.
      - Note: logging is console-only today (`AddSimpleConsole`), so this adds a rolling file sink.
      - **Recommended implementation:** Serilog (`Serilog.AspNetCore` + `Serilog.Sinks.File`) with
        `rollingInterval: Day`, `fileSizeLimitBytes ~10 MB`, `rollOnFileSizeLimit: true`,
        `retainedFileTimeLimit` from the config and a console sink keeping the existing
        `yyyy-MM-ddTHH:mm:ss.fffzzz ` format. Live level switching via a singleton
        `LoggingLevelSwitch` (`PUT /api/logs/level`) persisted in a new `logs-config.json` in the
        content root (same pattern as `schedules-config.json`) so it survives restarts, default
        `Information`. Viewer backfill from a bounded in-memory sink (custom `ILogEventSink` +
        capped `ConcurrentQueue`, ~2,000 entries) exposed by `GET /api/logs?after=<seq>`, with live
        streaming through a SignalR hub at `/hubs/logs` mirroring `ScanHub`. Serilog rolls but does
        not compress, so a background timer zips closed logs to `logs/archive/<date>.zip`
        (`System.IO.Compression`) and deletes archives older than the retention age. Config shape
        `{ level, retentionDays, fileSizeLimitMB, archiveEnabled }`; all log endpoints Admin-only.
      - **Implementation plan:** `.project_documents/PLAN-sprint5-activity-and-logs.md` (same shared
        batch as Activity #1; do not start until approved).
- [x] **3. Media-type-scoped scan** (moved from Sprint 4 #4) — **Done 2026-11-18.** The Scan button
      now passes the selected library id and is disabled when none is selected. `POST /api/scan`
      accepts an optional `{ libraryId }`, looks up that library's `MediaKind` (404 if missing) and
      scans only roots of the same type; an absent/null id keeps the legacy all-roots scan. Anime
      matches only Anime roots. Activity entries record the chosen scope. Verified in sandbox:
      Movie library → 1,877 items, Show library → 36,498 episodes, no-body scan → all 38,375
      roots' items, with matching `ScanStarted` activity details. 137/137 tests green.
      - **Analysis / implementation plan:** `.project_documents/PLAN-sprint4-wizard-and-scoped-scan.md`
        (shared batch with Sprint 4 #1). Promoted with `promote.sh --prod-only`; prod bundle
        `assets/index-DnVNfPJu.js`.
- [x] **4. Multiselect mode** (moved from Sprint 4 #7) — **Done 2026-11-18.** The multiselect control
      is an icon at the end of the expanded filter row; checkbox overlays appear only in select mode.
      Select all/Clear and a bulk bar offer `Mark for library` [No Change, Mark, Unmark] and
      `Hide from Library` [No Change, Hide, Unhide]; Apply is enabled when ≥1 item is selected and at
      least one action is not No Change. Bulk changes use the existing approve/hide APIs and show one
      summary toast. The collapsed row shows only `Show`; `Hide` exits multiselect.
      - **Analysis / implementation plan:** `.project_documents/PLAN-sprint5-multiselect-match-invite.md`
        (shipped with Sprint 5 #5 and Bug #1).
- [x] **5. Match tooling + admin promotion** (moved from Sprint 4 #8) — **Done 2026-11-18.** Admin-only
      magnifying-glass Match icon (tooltip `Match`) on unmatched movie, TV and anime cards, with a
      prefilled/editable search, automatic TMDB lookup and manual re-search. `Unmatched Movies`,
      `Unmatched TV Shows` and `Unmatched Anime` are the last options in their view dropdowns; manual
      matches persist across rescans via `SourceItem.ManuallyMatched`. The Users page can promote an
      existing user to Admin; the role endpoint blocks self-demotion and last-admin demotion.
      Originally described as: admin-only `Match` button on shows that searches TMDB using the first
      3 words of the title with an editable search field; the Unmatched view was a separate admin-only
      filter-bar toggle. Also allow promoting an existing user to Admin (Users page; wife and a
      coworker).
      - **Analysis / implementation plan:** `.project_documents/PLAN-sprint5-multiselect-match-invite.md`
        (shipped with Sprint 5 #4 and Bug #1).

- [x] **6. Card action consistency + icon states** — **Done 2026-11-17 (shipped with Sprint 4 #1).** (`Mark for Library` is not always in the same
      spot from card to card; pin the action row (Mark / Hide) to one fixed position so it lines up
      on every poster. Replace the text buttons with icons that convey state (keep tooltips and
      `aria-label`s):
      - **Mark:** gray check = not marked → click turns it **yellow** (marked, not yet synced) →
        **green** once the item is in the library after Sync.
      - **Hide:** eyeball icon = visible → click turns it **yellow** to mean hidden (persist this
        state on Sync — wire it into the per-library hide implementation in Sprint 4 #1). When the
        `Hidden <Type>` filter is active, hidden items show an eyeball with a **red circle-slash**
        to signal "currently hidden".
      - **Implementation reference:** `.project_documents/RESEARCH-batch1-filter-and-icons.md`.
- [x] **7. Library grid performance — lazy images + virtualization** — **Done (grid windowing) 2026-11-17.**
      The library page loaded slowly and Safari eventually reloaded it with a memory warning ("this
      page was reloaded due to memory usage"). Cause: `PosterGrid.tsx` calls `api.listSourceItems()`
      and keeps **every** source item in state (82,490 rows in the Kids Movies catalog), filters in
      memory, and used to mount a full card + raw `<img src={posterUrl}>` (no `loading`, no viewport
      gating, no windowing) for every filter result. Reference implementations (checked 2026-11-17):
      - **Sonarr/Radarr** (`package.json`): `react-lazyload@3.2.1` for poster mounting with an
        `offset`/placeholder so images only fetch near the viewport, plus `react-virtualized@9.22.6`
        and `react-window@1.8.11` for windowed grids — only the visible rows exist in the DOM.
        They also use `react-measure`/`react-use-measure` for cell sizing and
        `@tanstack/react-query` to cache repeat visits.
      - **Seerr** (`package.json`): `react-intersection-observer@10.0.3` for lazy image loading,
        `swr@2.4.1` for caching, Next.js `<Image>` (intrinsically lazy + resized) and its own
        image-proxy endpoint so TMDB posters are served pre-scaled and cached.
      - **Shipped (no new dependency):** a hand-written `LazyMount` wrapper in `PosterGrid.tsx` uses
        an `IntersectionObserver` with `rootMargin: "1200px 0px"` to mount each card only while it is
        within 1200px of the viewport and unmount it again once it scrolls away, with an
        aspect-ratio skeleton placeholder so the grid height stays stable. Both card types wrap in
        it; posters are served at TMDB `w185` via a `posterThumb()` URL rewrite
        (`/t/p/w500/` → `/t/p/w185/`) and carry `loading="lazy"`, `decoding="async"`,
        `draggable={false}`. Verified in-browser on the 3,987-card Kids Movies grid: 3 mounted
        posters at rest, 6 after scrolling 19,283px, DOM divs ~16k instead of ~35k+.
      - **Still open (follow-up):** (a) server-side paging / windowed fetching — the full catalog is
        still downloaded and held in memory → see **item 8** and
        `.project_documents/RESEARCH-library-grid-paging.md`; (e) optional `content-visibility: auto`;
        (f) a longer Safari soak test.
- [x] **8. Server-side paging & filtering** — **Done 2026-11-17.** The grid no longer downloads the
      whole catalog: `GET /api/library-entries` returns a 60-entry page (clamped 1..200) with
      server-side `q`/genre/rating/studio/network/letter/view filtering, pre-grouped movie + show
      entries and `approved`/`approvedAt`/`hidden`, and `GET /api/library-facets` supplies dropdown
      options. Pure `Curatarr.Engine/LibraryGrid/` helpers are covered by a new `Curatarr.Tests`
      xUnit project (44/44 passing). `PosterGrid.tsx` fetches page 0 on filter change (250 ms search
      debounce), appends pages with an IntersectionObserver sentinel and dedupe, and keeps LazyMount,
      posterThumb, the letter rail, card icons and the `All/Library/Unmarked/Hidden <Type>` filter.
      Feature flag `VITE_LIBRARY_PAGING` defaults on; `=false` restores the legacy path. Verified in
      sandbox: 77 KB entries + 49 KB facets payloads (was 16-30 MB), 3 posters mounted at rest,
      scroll append 3 → 6 with total unchanged, Library/Hidden views correct, no console errors.
      Promoted to prod with `scripts/promote.sh --sandbox-only` → `--prod-only`. Full design and test
      plan in `.project_documents/RESEARCH-library-grid-paging.md`.
- [x] **9. Hide icon colour parity with the checkmark** — **Done 2026-11-17 (shipped with the S5 #9 batch).** The Hide click was intended to use the
      same 3-state colour coding as the Mark checkmark, not just gray → yellow + red slash. Constrain
      the eyeball to **gray = visible**, **yellow = hidden (pending / not yet reflected by Sync)**,
      **green = hidden and confirmed in the library's hidden state**; keep the red circle-slash
      overlay in the `Hidden <Type>` view so "currently hidden" is unmistakable. Tooltips and
      `aria-label`s must state the state and the next action. This is a small follow-up to Sprint 5
      #6 (shipped in batch 1) and a different UI concern from the batch currently in flight
      (Sprint 5 #8 paging/filtering), so **do not fold it into #8** — take it as a standalone item
      after #8, or bundle it with the next `PosterGrid.tsx` batch. `CardActions` in
      `Curatarr.Web/src/components/PosterGrid.tsx` is the single place to change.
      - **Library/Hidden are mutually exclusive:** a card can be in the library *or* hidden, not
        both. Clicking **Hide** on a card that is in the Library must pop a confirmation explaining
        that choosing Hidden removes it from the Library; clicking **Mark for Library** on a hidden
        card must similarly confirm that choosing Library removes it from Hidden. Only the user's
        confirmation applies the change. The removal from the other state **still requires a Sync**
        to take effect on disk (same as normal unapprove), so the confirm text must say that.
      - **Pinned action row:** keep the Mark/Hide icon row pinned to the exact same spot on every
        poster — same bottom alignment and fixed height on movie cards, show cards and LazyMount
        skeletons — so it doesn't drift with title length or metadata lines.
      - **Shipped 2026-11-17:** `HiddenItem.CreatedAt` is now exposed as `hiddenAt` on
        `/api/source-items?libraryId=` and `/api/library-entries`, so the eye uses the same
        `reconcileStatus` boundary as the check (gray → yellow hidden/pending → green hidden/synced),
        with the red circle-slash retained in the Hidden view. Switching Library↔Hidden shows a
        confirm explaining the other state is removed and that a Sync is still required; both changes
        are applied only after confirmation. `CARD_ACTION_ROW_STYLE` pins a 30px row to the bottom of
        movie cards, show cards and both skeleton variants. 59/59 tests green; sandbox verified the
        `hiddenAt` API, yellow eye + slash, and confirm wording; promoted to prod with
        `assets/index-XzBcLR7h.js`.

## Sprint 6

- [x] **UI — Loading state with animation instead of blank cards** — **Done 2026-11-19.** Animated
      `Loading library…` / `Loading more…` indicators with `role="status"`, reduced-motion guard,
      and no layout shift are used for initial load, library switch, page append, and skeleton.
      Sandbox-verified and promoted with `index-R4dyCEyO.js` / `index-fENfh4zq.css`. Added
      2026-11-18 at user request as a follow-up to Bug #2 (oversized posters); deliberately
      **not** implemented as part of the Bug #2 fix.

- [x] **1. Automatic approve / hide rules based on filters** — **Done 2026-11-17.** Curatarr is
      a curated media layer: it catalogs titles from source folders and the user marks which ones
      belong in each virtual library; Sync then creates the symlinks and Plex refreshes. Two use
      cases drive this item:
      - **Children's curated library** — a parent wants a safe, small library. Instead of marking
        hundreds of titles by hand, they set rules such as "approve everything with genre
        Animation or Family", "approve titles rated G/PG", "hide anything with genre Horror".
      - **Adult overwhelmed by choice** — they want a short, relevant list. Rules narrow the
        catalog automatically (e.g. favourite director, actor/cast, network, studio, rating) and
        hide the rest, with manual Mark/Hide still able to override at any time.

      **What will be added (concise):**
      - A per-library **Rules** section where an admin/member creates rules from the same filter
        fields already in the grid — director, genre, actor/cast, studio, network, rating, year,
        media type — with an action `Approve` or `Hide` and a human-readable summary.
      - Rules evaluate against catalog metadata and write the same underlying state as the manual
        buttons (`VirtualLibraryItem` approvals, `HiddenItem` rows), so existing Sync, Plex refresh,
        per-library scoping and the gray/yellow/green + eye icons keep working unchanged.
      - A **dry-run / preview** before applying ("N titles matched") and an explicit **Apply** so a
        rule never changes hundreds of items silently; each application records what changed.
      - Ordering and override semantics: manual decisions win over rules; later rules can override
        earlier ones; re-running a rule is idempotent and reports only net changes.
      - Rule fields are stored as generic field/operator/value triples so future filters added to
        the grid automatically become available to rules — no new schema per filter.
      - Depends on the per-library hide model and the facet/filter definitions from Sprint 4 #1 and
        Sprint 5 #8 (both Done). No work starts without approval.
      - **Implementation reference:** `.project_documents/RESEARCH-sprint6-auto-rules.md` (full
        spec: schema, endpoints, rule evaluation, precedence, guardrails and verification plan).
      - **Shipped 2026-11-17:** migration `AddLibraryRules` (rules + audit runs + `SetByRule` on
        approvals/hides); `GET /api/library-fields`, `GET/POST/PUT/DELETE
        /api/virtual-libraries/{id}/rules`, `POST .../rules/preview`, `POST .../rules/apply`;
        Rules page in the sidebar with field/operator/value/action builder, preview and Apply
        confirmation that reminds you to press Sync. Manual decisions win, later rules override
        earlier ones, and re-apply is idempotent. 54/54 xUnit tests green; sandbox verified rule
        creation, preview (`toApprove: 92`), Rules page, and Sync warning; promoted to prod via
        `promote.sh --prod-only` with bundle `index-TFBTsAsG.js`.

- [x] **Show quality variants in the text label when multiple copies exist** — **Done 2026-11-19.**
      Client-only quality parser adds ` — 4K / 1080p` style suffixes to card text labels when a
      title has multiple distinct qualities; single-quality titles have no suffix. Verified across
      all sandbox movie entries (only `The Dark Knight (2008)` qualifies) and promoted with
      `index-R4dyCEyO.js`. Added 2026-11-18 at user request, end of the Sprint 6 list; deliberately
      **not** implemented as part of the Sprint 5 batch.

## Medium-term

- [x] **TLS support** — **DONE 2026-11-19 (D2; documentation and sandbox validation only).** Added `.project_documents/TLS-REVERSE-PROXY.md`, covering the existing Tailscale HTTPS path plus Caddy/nginx/Traefik examples with Let's Encrypt. Curatarr remains HTTP behind the proxy; TLS is not terminated in .NET. Verified with a scratch self-signed Caddy proxy in front of sandbox `:5174`: HTTPS app/API access, SignalR negotiation, and expected bundles all worked. Production TLS/cutover was intentionally not applied and remains a separate later decision.
- [ ] **Multi-root scanning** — parallelize scanner across roots for large libraries
- [ ] **Watch state tracking** — record which items are watched/ignored per library
- [ ] **User onboarding polish** — email templates (SMTP sender done 2026-11-14:
      `SmtpEmailSender` + `Smtp__*` env config, Gmail app password, console fallback retained)
- [x] **Backup/restore** — **DONE 2026-11-19.** Documented SQLite backup/restore procedure at `.project_documents/BACKUP-RESTORE.md`, with the restore rehearsal verified on a scratch volume/port before prod promotion. **Group D tracker (added 2026-11-19):** this bullet also tracks the remaining deployment/operations work so the approved order is visible in one place:
      - **D0 — GitHub source publication:** DONE 2026-11-19 (public repo `https://github.com/michaelpeele/curatarr`, initial commit `8cc8cd3 Initial import of Curatarr`, `main` pushed and tracking `origin/main`). Not a ROADMAP line.
      - **D1 — local ops basics:** DONE 2026-11-19; backup/restore operator runbook at `.project_documents/BACKUP-RESTORE.md`, explicit `curatarr-api:${CURATARR_VERSION:-latest}` / `curatarr-web:${CURATARR_VERSION:-latest}` image tags (`ROADMAP.md:176`), and README/`.env.example`/compose port-name drift cleanup. Sandbox-verified and promoted with unchanged bundles `index-UEE7VSba.js` / `index-fENfh4zq.css`.
      - **D3 — registry / multi-arch CI:** DONE 2026-11-19 — GitHub Actions workflow `d9ad777` published GHCR images via CLI-triggered run `44442469640`; both API and web manifests were verified for `linux/amd64` and `linux/arm64` at `sha-d9ad777`. Semver and `latest` remain reserved for tag/release runs; local promotion is unchanged. Covers Docker Hub / GHCR images (`ROADMAP.md:660`) and ARM + AMD64 multi-arch CI (`ROADMAP.md:661`).
      - **V1 release bundle:** DONE 2026-11-19 — `docker-compose.release.yml` was added in commit `0c28d00`, tag `v1.0.0` was pushed, and workflow run `44449305935` published and verified `1.0.0`, `1.0`, `latest`, and `sha-0c28d00` for both GHCR images on `linux/amd64` and `linux/arm64`. A scratch release-compose stack was verified on port 15003 without touching production. Local promotion is unchanged; the all-in-one image remains a future unchecked ROADMAP item.
      - **D2 — TLS / reverse proxy:** DONE 2026-11-19 (documentation plus sandbox validation only) — reverse proxy guide with Let's Encrypt (`ROADMAP.md:627`) at `.project_documents/TLS-REVERSE-PROXY.md`; scratch HTTPS proxy verified app/API/SignalR against sandbox `:5174`. Production TLS/cutover was intentionally not applied. Last of the original Group D items.
      - **D4 — blue/green production with sandbox:** PENDING follow-on after D2 — parameterised stacks, host reverse-proxy switch point, standby smoke/rollback rehearsal. Not a ROADMAP line.
      See `.project_documents/PLAN-deployment-operations-hardening.md` and `.project_documents/REMAINING-WORK-GROUPS.md`. D0, D1, D2, D3, and the V1 release bundle were marked done only after verification/promotion; D4 remains pending.

## Queued / New Projects

- [ ] **MetaDatarr** — a local cache app (yet to be developed) that caches external metadata about
      shows, movies, and anime, with TMDB as the first metadata provider. **Status: pending design
      phase.** Once available it would feed Curatarr's TMDB metadata enrichment, removing direct
      external API dependency from the scan/reconcile path.

## Long-term / Ideas

- [ ] **Plex/Jellyfin integration** — read watch state, auto-reconcile on changes
- [ ] **PostgreSQL backend** — optional DB provider for multi-user scale
- [ ] **Webhooks** — notify on scan complete / new items / reconcilation
- [x] **Docker Hub / GHCR images** — DONE 2026-11-19 (D3): GHCR publishing via `.github/workflows/publish-images.yml`; verified `ghcr.io/michaelpeele/curatarr-api:sha-d9ad777` and `ghcr.io/michaelpeele/curatarr-web:sha-d9ad777`.
- [x] **ARM + AMD64 multi-arch CI** — DONE 2026-11-19 (D3): one OCI index per image containing `linux/amd64` and `linux/arm64`, verified with `docker buildx imagetools inspect`.
- [ ] **API token auth** — machine/client access for external tools
- [ ] **i18n** — non-English UI
- [ ] **All-in-one image (single container)** — future packaging item after the V1 release. Combine the API and web image into one image/container so users only pull and run one artifact. V1 ships two published images plus `docker-compose.release.yml`; this item remains open.

## Known Limitations (documented in README)

- No HTTPS by default (cookie auth over plain HTTP — reverse proxy required for prod)
- SQLite unencrypted — volume access = full data access
- API port 5000 exposed for debugging (can be removed from compose)
- `EnsureCreatedAsync` doesn't handle schema evolution (mitigation in place)
- **Plex tvOS Home hub caching** — the Apple TV app's *Home* screen keeps its "Recently Added"
  rows cached for the whole app session; it only reloads after a force-quit. The library's own
  Recently Added row refreshes when you toggle Home ↔ Library. The Plex **server** has the new item
  within ~1 s of Sync (verified via `GET /api/plex/sections/{key}/recent`), and the desktop app
  updates instantly, so this is a client-cache limitation with no server-side workaround (Plex
  exposes no API to invalidate client hubs). Workaround on tvOS: double-press TV/Home and swipe the
  Plex app closed.
