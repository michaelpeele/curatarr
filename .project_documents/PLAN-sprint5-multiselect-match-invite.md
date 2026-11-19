# Sprint 5 #4 + #5 + Bug #1 — Multiselect, Match tooling, Invite accept

**Status:** approved — **implementation in progress** (decisions recorded 2026-11-18).

This is the next mandated batch from `/home/michaelpeele/ai-projects/workflow.md`:

1. **Sprint 5 #4 — Multiselect mode**
2. **Sprint 5 #5 — Match tooling + admin promotion**
3. **Bug #1 — Invite accept leaves the user stranded**

Sandbox-first workflow applies: build once → `promote.sh --sandbox-only` → manual/Playwright verify → `promote.sh --prod-only`.

## 0. Batch decisions

- **In scope:** bulk Mark/Unmark/Hide/Unhide from posters; admin-only Unmatched view and Match flow; promoting an existing user to Admin from Users; fixing the post-invite-accept dead end.
- **Out of scope:** Calendar, Sprint 6 Loading-animation enhancement, Sprint 6 rules changes, post-boot media-type inheritance (previous batch).
- **Admin promotion** means the global Identity role (`Admin` / `Viewer`) on Users, not deployment promotion.
- **Implementation order:** Bug #1 first (small), then Multiselect (large UI), then Match tooling + admin promotion (backend-heavy, small migration). Ship together as one batch.
- **Bulk approve/unapprove:** loop the existing single-id endpoints client-side. Do not resurrect the deliberately deleted `BulkApprovalEndpoints.cs` / `approveMany` / `unapproveMany` (CHECKPOINT.md:10-23). Hide/unhide already accept arrays.
- **Show semantics:** approve/unapprove only the representative episode (one show symlink); hide/unhide all episode ids. A show is hidden only when all episodes are hidden.
- **Manual match wins:** a new `SourceItem.ManuallyMatched` flag survives re-scans; Sprint 6 rules only affect approval/hide and must not change matched metadata.

### 0.1 Decisions confirmed (2026-11-18)

1. **Invite-token test hook:** use the robust option — a test-only sink (file/endpoint) that captures the one-time invite token for `test-api.sh`. Do not scrape docker logs.
2. **Failed invite auto-login:** navigate to `/` normally; the user lands on the signed-out/login screen **with the success toast** (including the login email). No prefilled-email retry page.
3. **Multiselect Apply:** enabled when ≥1 card is selected **and at least one** of Mark/Hide is not `No Change` (Mark-only or Hide-only is valid). Disabled only when both are `No Change`.
4. **Filename-derived matching (Groundhog Day case):** `Groundhog.Day.1993.2160p.UHD.BluRay.X265-IAMABLE` currently fails because `LibraryScanner.ParseTitleYear` (`LibraryScanner.cs:293-298`, regex at `:338`) only recognises `Title (Year)`; the whole dotted filename is sent to TMDB with no year. Implement a shared title-query normaliser that replaces `.`/`_`/`+` separators, extracts a 4-digit year, strips quality/release tokens (2160p, UHD, BluRay, X265, release group, etc.) and returns a clean query (e.g. `"Groundhog Day"`, year `1993`). Use it for scan lookups and for the Match modal: prefill the editable search field from the normalised title/year, auto-attempt a search on open, and always allow a manual re-search when auto-match fails or TMDB is unavailable.
5. **Detail columns on match:** clear only the backfill fields — `GenreIds`, `Rating`, `ProductionCompanies`, `Networks`, `Director`, `Creators`, `Cast`. Keep `TmdbId`, `PosterUrl`, `Overview`, title and year from the applied match.
6. **Scan merge split:** when `ManuallyMatched == true`, freeze metadata fields (TmdbId, title, year, poster, overview, detail fields). Still update non-metadata fields: `SeasonNumber`, `EpisodeNumber`, `MediaType`, and path/`RelativePath`.
7. **Role endpoint:** add `test-api.sh` integration coverage in addition to the unit tests. Block self-demotion (including the original admin) and block removing the last Admin; never allow zero admins.
8. **Build/verification:** rely on `promote.sh --sandbox-only` for the build (no separate standalone build step). Unmatched toggle placement defaults to after the view dropdown; may be revisited in Sprint 6.
9. **Documentation first:** these choices are recorded here before implementation starts.

## 1. Current state

### Sprint 5 #4 — Multiselect mode

- `PosterGrid.tsx` has no grid selection state. The existing `selected` state is the detail modal (`SelectedItem`, `:328-330`, `:643`) and must not be reused.
- Toolbar: sticky container `:1535-1556`; collapsed `:1557-1567`; expanded `:1568-1689`; “Filter:” label `:1570-1572`.
- Card actions: `CardActions` `:142-304`, fixed `CARD_ACTION_ROW_STYLE` `:119-127`; rendered by `PosterCard` `:1951-1965` and `ShowCard` `:2112-2126`.
- State: `items`/`approvedIds`/`hiddenIds`/`viewFilter` `:633-674`; one-key busy flags `approvingKey`/`hidingKey` `:638,:641` are unsuitable for bulk.
- Keys: `libraryEntryKey()` `:587-589` (`movie:` / `show:`). Movies use `item.id`; shows use `group.key`, with `representativeId` for approve/unapprove and `episodeIds` for hide/unhide (`entryToShowGroup` `:563-585`; `showGroups.ts:171-173`).
- Existing handlers: `handleApprove` `:1134-1150`, `handleUnapprove` `:1170-1186`, show variants `:1188-1231`, `changeHidden` `:1236-1286`; all toast per item, so bulk needs quiet variants or one summary toast.
- API: `hideItems`/`unhideItems` already accept arrays (`client.ts:440-449`); `approve`/`unapprove` are single-id (`client.ts:648-658`). Endpoints and auth are in `Program.cs:1161-1391`; `/api/reconcile` `:841-864` applies pending changes.
- `BottomBar.tsx` has no selection awareness; global bar `:115-127`, mobile fixed `:119-126`. Selection state should live in `PosterGrid`.

### Sprint 5 #5 — Match tooling + admin promotion

- No persisted match status exists. `SourceItem.TmdbId` is nullable (`Models.cs:57`); “PartialMatch” is only a transient scan event (`LibraryScanner.cs:10-16`, `:114,:119,:246`). `PROGRESS.md:137-147` records ~155 rows with `TmdbId IS NULL`, the natural Unmatched seed.
- `IMetadataProvider` (`MetadataProvider.cs:40-47`) only returns one best match (`SearchMovieAsync` `:99-115`, `SearchShowAsync` `:117-131`); there is no candidate-list method. `FakeMetadataProvider` `:327-360` uses exact-title lookup and null details.
- `POST /api/scan` (`Program.cs:741-811`) overwrites `TmdbId`, poster, overview, etc. on every scan (`:787-796`), so a manual match needs a persisted flag and skip-on-rescan behavior.
- Grid views are `all/library/unmarked/hidden` (`LibraryGridEndpoints.cs:15`, `LibraryEntriesQuery.cs:141-148`, `PosterGrid.tsx:325,1573-1586`). The view dropdown must stay at four options; Unmatched is a separate admin-only toggle.
- `App.tsx:243-255` derives `isAdmin` but does not pass it to `PosterGrid` (`:344-352`). `UsersPage.tsx:25-53,167-236` has invite/edit/password/delete but no global role control. `GET /api/users` (`Program.cs:379-384`) omits roles; no `PUT /api/users/{id}/roles` exists (despite stale TECHNICAL.md:40-47).
- `DetailModal.tsx:17-42` shows metadata but has no Match UI; `ShowCard` has a refresh affordance at `:2034-2064` where a Match button can live.

### Bug #1 — Invite accept

- `AcceptInvitePage.tsx:38-56` only sets `resultMessage` after `api.acceptInvite`; success render `:64-70` is an inert `<p>`. No navigation, login call, or auth refresh.
- `/accept-invite` is a sibling route to `<App>` (`main.tsx:13-20`), so `App.tsx:46-60` cold boot never runs there; `/api/me` is never refreshed.
- `Program.cs:297-327` confirms email and sets password but does not issue an auth cookie (compare login `:237-243`). The accept endpoint returns only `{ message }` at `:326`.
- Primary defect is missing navigation; lack of cookie is secondary. Minimal fix should reuse the already-tested login path rather than adding server-side sign-in.

## 2. Proposed changes

### Part A — Bug #1 (invite accept)

1. `Program.cs` accept handler (`:323` and `:326` success payloads): include `email = user.Email`. Keep the existing message; no cookie change.
2. `client.ts:517-521`: change `acceptInvite` response type to `{ message: string; email: string }`.
3. `AcceptInvitePage.tsx`: import `useNavigate` and `useToast`; after success:
   - `showToast(result.message, "success")` (ToastProvider wraps Routes in `main.tsx:13-21`, so it survives navigation);
   - `try { await api.login(result.email, password); } catch { /* toast explains sign-in email */ }`;
   - `navigate("/", { replace: true })` in both cases.
4. Navigating to `/` mounts `<App>`, whose cold probe calls `api.me()` and populates `me`/`isAdmin` correctly for the new Viewer. No prefill is required, but the email can be passed via route state if desired later.
5. `test-api.sh` (sandbox only per header `:8-15`): add an opt-in section after step 9 — read the one-time token from a **test-only token sink** (a sandbox-scoped file/endpoint that captures the generated token; do **not** scrape `EmailSender.cs:20-26` console logs), POST `/api/auth/accept-invite`, assert 200 + `email`, login with a fresh cookie jar, GET `/api/me` contains `Viewer`, token replay returns 400, cleanup with admin DELETE.

### Part B — Multiselect mode

1. `PosterGrid.tsx`: add `selectionMode: boolean` and `selectedKeys: Set<string>` keyed by `libraryEntryKey()`. Keep `selected` for the detail modal. Clear/prune selection on library/media-type/view/filter changes (`:1109-1123`) and on page-0 refetch.
2. Add a Multiselect toggle beside “Filter:” (`:1570-1572`) and an exit control in the collapsed toolbar (`:1557-1567`), using `toolbarButtonStyle` (`:344-354`). Disable/hide it when `libraryId == null` (browse mode has no `CardActions`).
3. Pass `selected`/`onToggle` to `PosterCard` and `ShowCard`; overlay a checkbox on the poster, suppress `onOpenDetails` in selection mode, and add Select all / Clear for currently loaded visible cards.
4. Render a bulk bar inside `PosterGrid` near the main return, using the `BottomBar` token pattern but its own z-index above the mobile bottom bar. It contains: selected count, `Mark for library` select (No Change / Mark / Unmark), `Hide from Library` select (No Change / Hide / Unhide), Cancel, and Apply; Apply enabled when ≥1 is selected **and at least one** action is not `No Change` (Mark-only or Hide-only is valid), disabled only when both are `No Change`.
5. Apply with existing APIs: movies → `item.id`; shows → representative id for Mark/Unmark, all episode ids for Hide/Unhide. Hide/unhide are one call each; approve/unapprove loop single-id methods via `Promise.allSettled`. Add `bulkBusy` separate from `approvingKey`/`hidingKey`; emit one summary toast; refresh approved ids, page 0 and facets once at the end.
6. Confirm only conflicting combinations (Mark+Hide, Unmark+Unhide), mirroring the wording at `:207-211` and `:223-227`. Pending hides remain visible/yellow and pending unhides remain in Hidden until Sync; never locally move cards on bulk hide.

### Part C — Match tooling + admin promotion

1. **Engine metadata search**
   - Add a shared title-query normaliser (e.g. `TitleQueryParser.Parse(string)` returning `{ Title, Year? }`) used by both the scanner and the Match modal: replace `.`/`_`/`+` separators with spaces, extract a 4-digit year (including from dotted filenames), strip quality/release tokens (2160p, UHD, BluRay, X265, release group, etc.), collapse whitespace and trim punctuation. `Groundhog.Day.1993.2160p.UHD.BluRay.X265-IAMABLE` must become `("Groundhog Day", 1993)`. Keep the existing `Title (Year)` regex working; extend `ParseTitleYear` (or its callers at `LibraryScanner.cs:106,198`) to use the normaliser before searching.
   - Add `TitleSearchResult` DTO (TmdbId, Title, Year?, PosterUrl, Overview).
   - Add `SearchTitlesAsync(query, MediaKind, year, limit, ct)` to `IMetadataProvider`; keep existing single-match methods untouched.
   - Implement in `TmdbMetadataProvider` using `search/movie` / `search/tv`, returning up to ~10 candidates; reuse `ImageBase`, `TryParseYear`, and private `TmdbMovie`/`TmdbTvShow` records.
   - `FakeMetadataProvider` returns an empty list by default so a no-key sandbox shows a clear “TMDB search unavailable” state. Add injectable candidates for unit tests if needed.
2. **Persist manual matches**
   - Add `SourceItem.ManuallyMatched` (bool, default false) in `Models.cs` and generate a small EF migration (no index needed).
   - In `/api/scan` upsert (`:787-796`), split the merge: when `existing.ManuallyMatched` is true, freeze **all metadata fields** (`TmdbId`, title, year, `PosterUrl`, `Overview`, `GenreIds`, `Rating`, `ProductionCompanies`, `Networks`, `Director`, `Creators`, `Cast`); still update non-metadata fields (`SeasonNumber`, `EpisodeNumber`, `MediaType`, path/`RelativePath`). This makes manual wins robust across re-scans.
3. **Unmatched filter (server-side, so totals/paging stay correct)**
   - Add `GridFilters.IsUnmatched` (`TmdbId is null or <= 0`) and apply it in `LibraryEntriesQuery` and `LibraryFacetsQuery`.
   - Add `unmatched=true` query param to `GET /api/library-entries` and `GET /api/library-facets`, validated like existing params; extend `LibraryEntriesParams` in `client.ts` and `pageParams` in `PosterGrid.tsx:684-696`. Include it in active-filter count and Clear filters (`:1476-1489`, `:1661-1677`).
4. **Match endpoints (Admin-only, with activity record)**
   - `GET /api/metadata/search?query=&mediaType=&year=` → `{ available, results }`; no TMDB key ⇒ `available:false, results:[]`.
   - `POST /api/metadata/match` body `{ sourceItemId?, representativeId?, tmdbId, mediaType, title?, year?, posterUrl?, overview? }`. Resolve affected rows server-side: for a movie/episode use `sourceItemId`; for a show use the representative’s `ShowTitle` + `SourceRootId` + `MediaType` (do not trust capped `EpisodeIds`). Set `TmdbId`, poster/overview/title/year from the applied match, set `ManuallyMatched=true`, clear **only the backfill fields** (`GenreIds`, `Rating`, `ProductionCompanies`, `Networks`, `Director`, `Creators`, `Cast`) so `DetailsBackfill` re-runs (keep TmdbId/PosterUrl/Overview/title/year), and record `ActivityCategory.Source`.
5. **Web**
   - `App.tsx:344-352`: pass `isAdmin` into `PosterGrid`; add optional prop in `PosterGrid.tsx:317-323`.
   - Add the admin-only Unmatched toggle in the filter bar after the view dropdown (`:1586`) or beside search (`:1650`); keep the four-option view dropdown unchanged.
   - Add a `Match` button on `ShowCard` (near `:2034-2064`) only when `isAdmin && unmatchedOnly && entry.kind === "show"`. Open a modal with an editable search field prefilled from the shared normaliser applied to the group title/filename (`TitleQueryParser.Parse`, e.g. `"Groundhog Day"`, year 1993), auto-attempt a search when the modal opens, and always allow manual re-search (including when auto-match fails or TMDB has no key). Show candidate posters/titles/years, and Apply/Close.
   - `UsersPage.tsx`: add a global role select; `client.ts`: extend `AppUser` with `roles`; add `setUserRole(id, role)`.
6. **Role endpoint**
   - `GET /api/users`: include `roles`.
   - `PUT /api/users/{id}/roles` body `{ role: "Admin" | "Viewer" }`, Admin-only, validates the role, prevents self-demotion (including the original admin) and removing the last Admin — never allow zero admins — updates `UserManager` roles, and records activity. Add `test-api.sh` integration coverage for the endpoint, self-demotion and last-admin guards in addition to unit tests. Update stale TECHNICAL.md endpoint table.

## 3. Tests

Baseline: 137/137 tests green. Add focused unit tests in `Curatarr.Tests` via the .NET 10 SDK container:

1. `GridFilters.IsUnmatched` accepts null/0/negative and rejects positive TmdbId.
2. `LibraryEntriesQuery` honors `unmatchedOnly` with correct grouping and hidden/library partition.
3. `LibraryFacetsQuery` computes facets from the unmatched subset when toggled.
4. Title-query normaliser handles `Title (Year)` and dotted filenames, strips quality/release tokens, extracts the year, and produces `("Groundhog Day", 1993)` for the regression case.
5. `TmdbMetadataProvider` candidate mapping/result limit via an injected fake HTTP handler if practical.
6. Role validation helper rejects unknown roles, self-demotion (including the original admin), and last-admin demotion. Role endpoint integration coverage in `test-api.sh` (sandbox only).
7. Manual-match merge keeps metadata when `ManuallyMatched=true`, updates otherwise.
8. Invite accept response mapping if extracted; otherwise covered by the sandbox shell test.

No frontend test runner exists; UI verification is manual/Playwright.

## 4. Sandbox verification

1. Rely on `./scripts/promote.sh --sandbox-only` to build both images (no separate standalone build step; `--no-cache web` only if the bundle looks stale).
2. `./scripts/promote.sh --sandbox-only`; confirm `:5001/api/bootstrapped` and UI `:5174`.
3. `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`; then full `./test-api.sh` for invite/accept and role checks. Never run full suite against prod.
4. Manual/Playwright checks on `:5174`:
   - **Invite:** invite a fresh user, open the console-email link, submit name/password, confirm toast + automatic redirect into the app, `/api/me` shows Viewer, token replay 400. Repeat with alternate email and confirm the toast states the login email.
   - **Multiselect:** toggle Multiselect, select all/clear, bulk Mark/Unmark and Hide/Unhide; confirm one summary toast, representative-only approve, all-episode hide, yellow pending hide until Sync, green after Sync, Viewer 403, no mobile overlap with `BottomBar`.
   - **Match:** Unmatched toggle is admin-only; four-option dropdown unchanged; Match appears only for admin + unmatched shows; editable search field prefilled from the normalised title/filename with auto-attempt and manual re-search; no-key state shows the unavailable message; applying a direct match updates all rows of a multi-episode show, survives re-scan, and is unchanged by rule Apply; `/api/me` reflects role promotion and last-admin guard blocks self-demotion.
5. Check browser console: 0 page errors. Record manual verification in `PROGRESS.md`.
6. `./scripts/promote.sh --prod-only`; verify new bundle hash (before batch `assets/index-DnVNfPJu.js`) and hard-refresh note.

## 5. Risks and decisions

- Client-side bulk approve loops can partially fail; use `Promise.allSettled`, report counts, and refresh once. No new bulk approval backend.
- Show groups split if only some episodes are matched; apply matches to all rows resolved server-side, never from capped `EpisodeIds`.
- `TmdbId == 0` must count as unmatched. Clearing only the backfill columns triggers backfill after a match while keeping the applied TmdbId/poster/overview.
- Manual-match flag needs a migration and scan-merge guard; otherwise the next scan silently reverts it.
- Dotted release filenames (e.g. `Groundhog.Day.1993.2160p.UHD.BluRay.X265-IAMABLE`) currently parse as a single title with no year; the shared normaliser must be applied in both scan and Match flows, with a unit-test regression.
- Role changes are security-sensitive: last-admin and self-demotion guards are mandatory (including the original admin); role response must not leak password data; integration coverage in `test-api.sh`.
- Invite auto-login may fail for the alternate-email case; still navigate and rely on the toast to explain the login email. No server-side cookie issuance in this batch.
- Sandbox has no TMDB key; verify search-unavailable UI separately and use direct `/api/metadata/match` for apply/rescan verification.

## 6. Acceptance criteria

- Both Settings/nav pages remain unaffected; Multiselect works from the filter bar with all bulk states and Sync semantics intact.
- Admin-only Unmatched toggle and Match flow work; manual matches persist across scans; admin promotion works with guards.
- Invite accept redirects into the app as Viewer with a clear toast; no stranded page.
- All tests and builds green; sandbox verified before prod; docs and plan status updated after promotion.
