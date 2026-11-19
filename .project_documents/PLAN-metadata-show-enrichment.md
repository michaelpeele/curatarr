# Metadata / show enrichment — analysis and recommendation

**Status: APPROVED / CLOSED (2026-11-19).** The user confirmed backdrops are the large banner images and are not needed. No code was changed. Group C is retired as code work: Calendar already delivers the next-episode goal, episode drill-down was retired earlier, and backdrops are not being added.

## 1. Purpose

This analysis covers the remaining Group C items after Group B and its follow-up were promoted:

- `ROADMAP.md:184` — **TMDB metadata enrichment polish** — posters, backdrops, genres displayed in UI grids.
- `ROADMAP.md:162` — **Show-level metadata** — air-date / last-updated fields, per-show endpoint, episode drill-down.

The user has since clarified the intent:

> "we decided earlier to pivot from any episode level drill down, main thing was to bubble up when the next episode is on in the calendar."

That pivot materially changes the value of the remaining work. The Calendar already bubbles up the next episode air date. This document recommends closing the remaining show-metadata work rather than building a new show entity or episode drill-down.

## 2. What is already shipped

### Calendar next-episode behavior (Calendar slice 1 and 2)

Confirmed from `.project_documents/PLAN-calendar-from-metadata.md` and the current code:

- `SourceItem` has explicit `ReleaseDate`, `AirDate`, `AirSeasonNumber`, `AirEpisodeNumber`, `AirIsSeasonPremiere`, and `DateDetailsParsedAt` columns (`Curatarr.Engine/Data/Models.cs:86-96`).
- Dates are parsed from cached `TmdbRawDetails.Json` by `CalendarDateParser`; there are no live TMDB calls in the Calendar path.
- TV uses `next_episode_to_air.air_date`. A next episode with `episode_number == 1` renders `New Season (#)`.
- Calendar de-duplicates show/anime rows on `(MediaType, TmdbId, date)` because episode rows share the same show-level next-episode object.
- `GET /api/calendar/events` returns `{ timeZone, today, month, events }`. The endpoint supplies `timeZone`/`today` because `/api/schedules/settings` is admin-only and Calendar is available to all authenticated users.
- `CalendarPage.tsx` renders dated cards with poster/title/date; slice 2 added the All-view `CalendarItemModal` using existing `VirtualLibraryItem`/`HiddenItem` APIs and exact PosterGrid wording.
- The shared `BottomBar` still controls Scan/Sync and `Sync applies to all libraries.` remains visible.

**Conclusion:** the user's stated main goal — surfacing when the next episode is on in the Calendar — is already delivered and promoted.

### Existing TMDB enrichment

Also already shipped:

- Posters (`PosterUrl`) on `SourceItem` and in Calendar/library cards.
- Genre IDs (`GenreIds`) with genre filtering in the library grid.
- Rating, studio/network, director/creators, and cast columns, backfilled from TMDB and used by filters/search.
- `TmdbRawDetails` staging table with the full raw TMDB response per `(mediaType, tmdbId)`.

`TMDB metadata enrichment polish` is therefore partly stale. The only clearly unshipped piece from its text is **backdrops displayed in UI grids**; there is no `BackdropPath` column or client field today.

## 3. Findings

### Group C item 1 — Show-level metadata

The remaining sub-items are:

- last-updated field — no current user-visible requirement; the user's priority was the next-episode date, which is already present.
- per-show endpoint — originally motivated by per-card refresh that currently refetches the whole catalog. It is an optimization, not the stated goal.
- episode drill-down — explicitly out of scope in the original item and now explicitly retired by the user.

**Finding:** this item should be closed as superseded by Calendar slices 1 and 2. Adding a real show entity, a per-show endpoint, or episode drill-down would be new scope with no confirmed acceptance need.

### Group C item 2 — TMDB metadata enrichment polish

Posters and genres are already displayed/filterable. Backdrops are not.

**Finding:** this item is not entirely empty, but it is no longer a natural pairing with show metadata. If pursued, it should be treated as a separate, optional UI/data enhancement: add a backdrop field to the backfill and a single display surface, then verify it. It should not be bundled into a "show metadata" group just because both touch metadata.

## 4. Recommendation for approval

**Recommended decision: retire Group C as a code work group.**

1. Close `ROADMAP.md:162` **Show-level metadata** as superseded/completed by Calendar slice 1's date foundation, with a note that episode drill-down and per-show endpoint were retired by user decision.
2. Re-scope `ROADMAP.md:184` **TMDB metadata enrichment polish** to a separate, optional backlog item whose only remaining candidate is a backdrop field/display; do not implement it as part of Group C.
3. Move to **Group D — Deployment and operations hardening** next: TLS (`ROADMAP.md:616`), Docker/container cleanup (`ROADMAP.md:173`), backup/restore (`ROADMAP.md:621`), Docker Hub/GHCR (`ROADMAP.md:635`), multi-arch CI (`ROADMAP.md:636`).

Why this is the recommendation:

- It follows the user's explicit pivot.
- It avoids building unused infrastructure (show entity, episode drill-down, last-updated field).
- It avoids a misleading "metadata group" that is mostly already done.
- It keeps the next work aligned with operational needs rather than speculative UI metadata.

## 5. Alternate option if the user still wants a code group

If you want one more small UI/data group before deployment work, the narrow version would be:

1. Add a `BackdropPath` column to `SourceItem` and parse/backfill it from cached `TmdbRawDetails.Json` (same raw cache, no new live TMDB call).
2. Add `backdropUrl` to the relevant DTO/client types.
3. Display the backdrop in exactly one agreed surface — recommended: the existing `DetailModal` header, not library cards.
4. Reuse the existing details-backfill status and toast behavior.

This would be a new group, not a continuation of show metadata. It would require Docker-only build, sandbox verification, manual browser checks, promotion, and bundle-hash comparison per `workflow.md`.

## 6. Verification / closeout plan if recommendation is approved

If Group C is retired with no code:

- Update `REMAINING-WORK-GROUPS.md` to mark Group C closed/retired and name Group D as the next natural group.
- Update this document status to `DONE`.
- Update `ROADMAP.md` only to add closeout notes for the two items, not to claim new implementation. No build/sandbox/promotion is needed because no code changes.

If the optional backdrop option is approved instead:

- Docs-first update to this plan and `PROGRESS.md`.
- Implement in the shared tree only.
- `docker compose build api web`.
- `./scripts/promote.sh --sandbox-only`; wait for `:5001/api/bootstrapped`; confirm smoke checks and `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
- Manual checks on sandbox `:5174` as `admin@example.com` / `Passw0rd!123`, including zero console errors and no regression to Calendar slice 2 or the Group B follow-up.
- Record manual verification in `PROGRESS.md`.
- `./scripts/promote.sh --prod-only` only after all checks pass and no code changed afterward.
- Hard-refresh prod and compare served JS/CSS hashes against the current promoted `index-UEE7VSba.js` / `index-fENfh4zq.css`.

## 7. Approval question

Do you want to:

1. **Approve the recommendation:** retire Group C as code work, close the two roadmap items with notes, and move next to Group D; or
2. Approve the optional backdrop-only group instead?
