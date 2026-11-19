# Calendar slice 2 — prompt and bundling analysis

Status: **slice 2 done** (2026-11-18). Slice 1, the follow-up UI-flow fixes, and slice 2 are all promoted to prod; ROADMAP Sprint 4 #2 Calendar is checked off.

## User request

> Provide a copy pasteable prompt for slice 2, analyze and document in a markdown first if one is not completed, see if anything else can be bundled together.

## Current state

Calendar slice 1 and its follow-up are complete:

- SourceItem date columns are backfilled from cached `TmdbRawDetails.Json`; no live TMDB calls.
- TV dates use `next_episode_to_air.air_date`; `episode_number == 1` renders `New Season (#)`.
- Calendar is available to all authenticated users.
- Server timezone comes from `Schedules:TimeZone` via `ScheduleTimeZone` and is returned by the Calendar endpoint.
- Calendar shares the library page toolbar/Sync behavior through the existing `<BottomBar>`.
- With `All libraries` selected: Sync stays enabled and shows `Sync applies to all libraries.`; Scan/Schedule are disabled with clearer titles; media type is unlocked. With a concrete library selected, media type switches to and locks at that library's type.
- Slice 2 (All-view link/Hide modal) is still planned.

## Bundling decision

Do **not** bundle any other remaining work group into Calendar slice 2.

| Candidate | Bundle? | Reason |
| --- | --- | --- |
| Sprint 5 #8 server-side paging/filtering | No | Needs explicit approval, separate data path, unrelated to Calendar modal. |
| Loading animation | No | Previously excluded from the original Calendar task; separate group. |
| Quality-variant labels | No | Previously excluded; separate group. |
| Responsive redesign | No | Previously excluded; slice 1 only needs iOS Safari-friendly UI, no redesign. |
| Groups C–G (metadata enrichment, infra, security, watch state, onboarding) | No | Separate surfaces and larger scope. |
| PosterGrid wording/API semantics | Yes, as reuse only | This is not another group; slice 2 is explicitly required to reuse the existing Mark/Hide wording and APIs. |
| Plan-doc cleanup | Yes, as docs-only | The plan example/props are stale and should be reconciled before/during slice 2 implementation. |

Only slice 2 should be implemented in the next batch.

## Facts that constrain slice 2

- `GET /api/calendar/events?month=&libraryId=&mediaType=` currently returns: `timeZone`, `today`, `month`, `events`.
- Each event currently returns: `id`, `mediaType`, `title`, `showTitle`, `date`, `posterUrl`, `seasonNumber`, `episodeNumber`, `isSeasonPremiere`, `tmdbId`.
- The API does **not** currently return `libraries`, `sourceItemIds`, `approved`, or `hidden` fields. The plan's example with `libraries: [{ id, name }]` is aspirational/stale.
- Calendar page props are currently: `libraries`, `selectedLibraryId`, `onSelectLibrary`, `libraryMediaType`, `refreshKey`. The plan's older frontend prop list is stale.
- `CalendarPage` event cards are read-only `div`s with no click handler. In All view, they need to become accessible buttons/clickable cards that open a modal.
- A show event is de-duplicated by `(MediaType, TmdbId, date)`, so one visible event can represent multiple episode SourceItems. Hide must support all episode ids, matching PosterGrid bulk hide behavior.
- Existing client APIs are available: `getApprovedItemIds`, `approve`, `unapprove`, `listHiddenItemIds`, `hideItems`, `unhideItems`.
- PosterGrid exact wording must be reused:
  - `Mark for library`
  - `In this library — click to unmark`
  - `Marked — click Sync to add to the library; click to unmark`
  - `Hide requested — click Sync to apply; click to cancel`
  - `Hidden and synced — click to unhide`
  - `Unhide requested — still hidden until Sync; click to cancel`
  - `Visible in this library — click to hide`
- PosterGrid confirmations mention that changes still need a Sync to take effect on disk and in Plex. Keep that wording.
- Keep the Calendar note `Sync applies to all libraries.`

## Recommended implementation decisions

1. Make only All-view event cards clickable in this slice. Concrete-library Calendar cards stay read-only to avoid ambiguous library targeting.
2. Extend the Calendar event DTO with `sourceItemIds` and preferably `representativeSourceItemId`. `sourceItemIds` should include all SourceItems represented by a de-duplicated show event, ordered with the metadata-bearing representative first.
3. The modal starts with `Choose a library…` and all actions disabled until a writable library is selected.
4. Writable libraries are: Admin sees all; non-admin only libraries with `myRole` Owner or Curator. Viewers cannot Mark/Hide.
5. Use existing approval/hidden APIs and state calculation. Do not introduce a parallel Calendar state model.
6. On success, refresh approved/hidden ids for that library and refresh Calendar data; show toasts using existing PosterGrid wording.
7. Keep the shared BottomBar unchanged.

---

## Copy-pasteable prompt

```markdown
Work only on Calendar slice 2: the All-view link/Hide modal. Do not start any other remaining group or bundled item.

First update documentation, then implement:
- Read `.project_documents/PROMPT-calendar-slice2.md`, `.project_documents/PLAN-calendar-from-metadata.md`, `.project_documents/REMAINING-WORK-GROUPS.md`, and `.project_documents/ROADMAP.md`.
- Update docs to mark slice 2 as in progress before code changes. Do not mark ROADMAP items done until after verification and promotion.
- Reconcile stale plan text where it shows Calendar response fields/props that do not match the implemented slice-1 API. The actual Calendar response is `{ timeZone, today, month, events }`; each event has `id`, `mediaType`, `title`, `showTitle`, `date`, `posterUrl`, `seasonNumber`, `episodeNumber`, `isSeasonPremiere`, `tmdbId`. Actual `CalendarPage` props are `libraries`, `selectedLibraryId`, `onSelectLibrary`, `libraryMediaType`, `refreshKey`.
- Keep this change scoped to slice 2 only. Do not implement server-side paging, loading animation, quality-variant labels, responsive redesign, infra work, or any other group.

Confirmed behavior/decisions from earlier slices:
- Calendar dates come from explicit SourceItem date columns backfilled from cached `TmdbRawDetails.Json`; no live TMDB calls.
- TV uses `next_episode_to_air.air_date`; `episode_number == 1` renders `New Season (#)`.
- Calendar is available to all authenticated users.
- Server timezone from `Schedules:TimeZone` drives `today`/month. `/api/schedules/settings` is admin-only, so keep relying on the Calendar endpoint's `timeZone`/`today`; do not call the admin settings endpoint.
- Calendar shares the library page BottomBar/Sync behavior. The note `Sync applies to all libraries.` must remain visible.
- With `All libraries` selected, Scan/Schedule remain disabled and Sync remains enabled. Do not change that behavior.

Backend:
- Extend `GET /api/calendar/events` minimally so the slice-2 modal has enough data to act through existing APIs.
- Add `sourceItemIds: string[]` and preferably `representativeSourceItemId: string | null` to each event. For movies this is the single movie SourceItem id. For shows/anime, include all SourceItems represented by the de-duplicated `(MediaType, TmdbId, date)` event, ordered so the representative item with season/episode metadata is first.
- Do not add a new approval/hidden state model. Keep using `VirtualLibraryItem`/`HiddenItem` and the existing endpoints.
- Do not add per-event `libraries` unless the UI truly needs it; the modal should use the existing libraries prop and per-library approved/hidden API calls.

Frontend:
- In `CalendarPage`, make event cards clickable only when `libraryFilter === ""` (All libraries). Concrete-library cards stay read-only in this slice.
- Clicking an All-view event card opens a new `CalendarItemModal` with keyboard-accessible button semantics.
- Modal shows event poster/title/date and a library picker starting at `Choose a library…`. Actions are disabled until a valid writable library is selected.
- Determine writable libraries from existing library role data: Admin can use all libraries; non-admin Owner/Curator libraries only; Viewers cannot Mark/Hide. If no writable libraries, show a read-only message.
- After a library is selected, load state with existing `api.getApprovedItemIds(libraryId)` and `api.listHiddenItemIds(libraryId)`. Never invent a separate Calendar state cache.
- Mirror PosterGrid semantics exactly:
  - Mark target for movies is the movie SourceItem id; for shows/anime it is the representative source item id.
  - Hide target for movies is the movie SourceItem id; for shows/anime it is all `sourceItemIds` for that event.
  - Use existing `api.approve`/`api.unapprove` and `api.hideItems`/`api.unhideItems`.
  - Use exact PosterGrid wording and Sync warnings: `Mark for library`, `In this library — click to unmark`, `Marked — click Sync to add to the library; click to unmark`, `Hide requested — click Sync to apply; click to cancel`, `Hidden and synced — click to unhide`, `Unhide requested — still hidden until Sync; click to cancel`, `Visible in this library — click to hide`.
  - Keep confirmations that say the change still needs a Sync to take effect on disk and in Plex.
  - Keep the Calendar note `Sync applies to all libraries.`
- On success, show existing-style toasts, refresh approved/hidden ids for the selected library, and refresh Calendar data.
- Keep the UI mobile/iOS Safari-friendly using existing CSS variables and touch-friendly targets. Do not perform a responsive redesign.

Verification:
- Build only through Docker: `docker compose build api web`.
- Run `./scripts/promote.sh --sandbox-only`; wait for `:5001/api/bootstrapped`; confirm smoke checks and `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
- Manually verify on sandbox `:5174` using admin `admin@example.com` / `Passw0rd!123`.
- Manual checks must include:
  - All-view Calendar cards open the modal and are keyboard accessible.
  - Concrete-library Calendar cards remain read-only and unchanged.
  - Modal starts with `Choose a library…` and disabled actions.
  - Library picker honors Admin/Owner/Curator/Viewer permissions.
  - Mark/Hide use existing APIs, exact wording, Sync warnings, and pending states.
  - Hide on a show event covers all represented episode ids.
  - `Sync applies to all libraries.` remains visible and Sync stays enabled in All view.
  - Month arrows/date jump, library filter, media-type lock behavior, and shared BottomBar still work.
  - Browser console has zero errors.
- Record manual verification in `.project_documents/PROGRESS.md`.
- Only after sandbox and manual checks pass, run `./scripts/promote.sh --prod-only` if no code changed after verification; otherwise rerun sandbox verification first.
- Hard-refresh/cache-bust prod and verify the new served JS/CSS bundle hashes against the previous `index-WyeMlDms.js` / `index-CoR6XILo.css`.

Acceptance criteria:
- Clicking an All-view Calendar entry opens a modal that can Mark/Hide through existing APIs with the usual Sync wording.
- Library selection is required before any Mark/Hide action.
- Hidden/approved state is read from and written through existing per-library APIs only.
- PosterGrid wording and Sync warnings are reused exactly.
- Concrete-library Calendar behavior and shared BottomBar/Sync behavior are unchanged.
- No other remaining group or out-of-scope item is started.
```
