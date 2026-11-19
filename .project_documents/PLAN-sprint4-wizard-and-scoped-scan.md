# Sprint 4 #1 + Sprint 5 #3 — Wizard media-type once & media-type-scoped scan (analysis / implementation plan)

**Status:** **implemented, verified and promoted 2026-11-18** (137/137 tests; prod bundle
`assets/index-DnVNfPJu.js`).

This is the shared batch for Sprint 4 **#1 "Wizard media-type once"** and Sprint 5 **#3 "Media-type-scoped scan"**. They are the next uncompleted entries in the canonical batch order in `/home/michaelpeele/ai-projects/workflow.md` and both touch media-type selection / scan flow, so they belong together. The sandbox-first workflow applies: build once → verify in sandbox → promote only after pass.

> **Numbering note:** `ROADMAP.md:343` labels the wizard item "Sprint 4 #1"; the workflow.md batch list calls it "Sprint 4 #2". Same work item — this plan uses the ROADMAP label.

---

## 1. Current state ( researched, no code changed)

### Sprint 4 #1 — wizard media type
- The first-boot wizard (`Curatarr.Web/src/components/FirstBootWizard.tsx`) has **no media-type choice in step 1**. Step 1 is Account (username / display name / password).
- Media type is asked **twice** today:
  - **Step 2 (Sources)** — a per-source-root `<select>` (`FirstBootWizard.tsx:308-321`), each row carrying `mediaType` (`WizardState.sourceRoots[]` :11, initial :23; "add root" hardcodes `"Movie"` :336).
  - **Step 4 (Library)** — a separate `<select>` bound to `wizard.libraryMediaType` (`WizardState` :15, initial :27; select :425-434).
- `handleSubmit` (`:194-218`) posts `api.bootstrap({...})` with `sourceRoots[].mediaType`, `libraryMediaType`, plus account/path/library fields; then logs in.
- Backend `BootstrapWizardEndpoints.cs`: `BootstrapAllRequest` (`:13-22`) carries `List<SourceRootDto>` (each with `MediaType`) and `LibraryMediaType`. Each is parsed independently with `Enum.TryParse<MediaKind>(..., ignoreCase:true)` and **silently falls back to `MediaKind.Movie`** on bad input (`:56-71` roots, `:86-98` library). One-shot guard at `:37-39`; single `SaveChangesAsync` at `:110`.
- `MediaKind { Movie, Show, Anime }` (`Models.cs:6`); `SourceRoot.MediaType` (:27), `VirtualLibrary.MediaType` (:127). **No global/default media type is persisted.**

### Sprint 5 #3 — media-type-scoped scan
- `LibraryScanner.ScanAsync(roots, onProgress, waitIfPaused, ct)` (`LibraryScanner.cs:45-49`) takes only source roots. Root dispatch at `:71-73`: Movie → `ScanMoviesAsync`, everything else (Show **and** Anime) → `ScanShowsAsync`. Per-root progress counters reset inside each scan (`:90`, `:187`).
- `POST /api/scan` (`Program.cs:745-798`) is Admin-only, takes **no request body**, and does `var roots = await db.SourceRoots.ToListAsync();` (`:752`) — **all roots, every media type, on every click** (the bug). Upsert is global by stable id; nothing is deleted.
- The client already knows the selected library's media type: `App.tsx:266-272` derives `selectedLibMediaType`, but `handleScan` (`:307-319`) calls `api.scan()` with **no args** and ignores it. `client.ts:556-560` `scan()` has no params. `BottomBar.tsx:25` `onScan: () => void`, Scan button `:199-203` has no `disabled={!selectedLibraryId}` guard.
- **No root↔library relationship exists** in the model — the only join key is matching `MediaKind` on `SourceRoot` and `VirtualLibrary`.

---

## 2. Proposed changes

### Part A — wizard media type once
1. **`FirstBootWizard.tsx`**
   - Add a single `mediaType: MediaKind` to `WizardState` + `initial` (default `Movie`) (`:6-28`).
   - Add one media-type select to **step 1**, after the password field (`:266`), with the same Movie/Show/Anime options.
   - **Remove** the per-source-root select and per-row `mediaType` (`:11`, `:23`, `:307-322`, `:336`) and the library select + `libraryMediaType` (`:15`, `:27`, `:425-434`). Step 2/4 copy should state the chosen type is inherited.
   - Update `handleSubmit` payload (`:199-208`) to send one top-level `mediaType`.
2. **`BootstrapWizardEndpoints.cs`**
   - Add a single top-level `MediaType` string to `BootstrapAllRequest` (`:13-22`). Decide compatibility: keep `SourceRootDto.MediaType` / `LibraryMediaType` as **optional** and ignore them, or remove them. Recommendation: accept the new single `mediaType` as authoritative; keep old fields optional and ignored so any stale caller doesn't 400 (the wizard is the only caller).
   - Parse once with `Enum.TryParse<MediaKind>`; on failure return **400** (tighter than today's silent `Movie` fallback) so a bad value is visible in the new flow. Apply the parsed kind to **every** created `SourceRoot` (`:56-71`) and the `VirtualLibrary` (`:86-98`).
3. **`api/client.ts` (optional, recommended):** strongly type `bootstrap` with a request/response interface instead of `Record<string, unknown>` (`:704-708`).
4. **`test-api.sh`:** update both hard-coded bootstrap JSON bodies (`:38` guard payload, `:62-64` happy-path payload) to the new single `mediaType`. Keep `libraryMediaType`/per-root fields out. `WIZARD_ONLY=1` must still pass.

**Decision point (out of scope by default):** post-boot creation in `SourcesPage.tsx` / `LibrariesPage.tsx` still asks for media type per item. Inheriting the wizard choice there would require a persisted global/default media type (new state + migration), which the ROADMAP item does not ask for. Default: leave those flows unchanged; note as a future enhancement.

### Part B — media-type-scoped scan
1. **`client.ts`:** `scan(libraryId?: string)` → `POST /api/scan` with JSON body `{ libraryId }` when provided (`:556-560`).
2. **`App.tsx`:** `handleScan` forwards `selectedLibraryId` to `api.scan()` (`:307-319`). `selectedLibMediaType` is already derived (`:266-272`) and can be used for the activity detail / guard.
3. **`BottomBar.tsx`:** change `onScan` to accept the selected library id (`:25`), pass it from the library selector, and disable the Scan button when no library is selected (`:199-203`), matching the Schedule button pattern (`:212-219`).
4. **`Program.cs`:**
   - Add a `ScanRequest(string? LibraryId)` DTO near the other request records (`:1471-1482`).
   - `POST /api/scan` accepts the body; when `LibraryId` is present, look up the `VirtualLibrary` and filter roots to `r.MediaType == library.MediaType` (`:752`) before `ScanAsync`. When absent (legacy callers / shell scripts), keep scanning all roots for backward compatibility.
   - Keep the Admin gate (`:798`) and enrich `ScanStarted`/`ScanFinished` activity details with the scoped media type / library name.
   - Do **not** conflate this with the Plex "path-scoped scan" work already shipped.
5. **Edge cases:** Anime uses the show-layout path — exact `MediaKind` match means an Anime library scans only Anime roots (not Show). Null `selectedLibraryId` (zero libraries) → no body / all-roots fallback as decided above. Unscanned roots' existing items remain (global upsert, no deletions). Progress `Total` collapses from repeated per-root sequences to one when scoped.

---

## 3. Tests

### Unit / integration (`Curatarr.Tests`, .NET 10 SDK container)
- Bootstrap: new single-`mediaType` payload creates source roots + library all with that kind; invalid kind returns 400; one-shot guard still holds.
- Scan scoping: a scoped request only upserts source items from matching-`MediaKind` roots (seed Movie + Show roots, scan with a Show library id, assert only Show items created); legacy no-body request still scans all.
- Scanner root dispatch: Movie → movie layout, Show/Anime → show layout (if a seam is added for testability).
- Existing `ScanControlServiceTests` (ETA), `ActivityEndpointsTests` (ScanStarted/Finished filters) must stay green.

### Sandbox verification
- Fresh sandbox: `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh` passes with the new payload (wizard happy path + guard). Manually confirm the wizard shows one media-type select in step 1 and none in steps 2/4.
- `test-scan-pipeline.sh` against `:5001`: register one Movie root and one Show root; scan with a Movie library selected → only Movie items; scan with a Show library → only Show items; no-body scan → both.
- Activity page shows enriched ScanStarted/Finished details; 0 browser console errors. No prod changes until `--prod-only`.

---

## 4. Risks / decisions
- **Backward compatibility:** keeping the old bootstrap fields optional/ignored and the no-body scan fallback avoids breaking shell scripts and any cached client. Choose whether to keep or drop them explicitly during implementation.
- **Validation strictness:** switching bootstrap from silent `Movie` fallback to 400 changes behavior for malformed input; acceptable since the only caller is the updated wizard.
- **No persisted global default:** post-boot forms still ask per item — documented, not fixed here.
- **Progress shape change:** scoping to one root yields a single 1..N progress sequence instead of repeated ones; the UI already handles arbitrary `Completed`/`Total`.
- **Anime vs Show:** exact match is intentional; document it so "TV library → shows only" doesn't surprise Anime users.

## 5. Acceptance criteria
- First-boot wizard asks for media type **once** (step 1); library and all source roots inherit it; bootstrap endpoint persists one kind everywhere.
- Scan is scoped to the selected library's media type; a selected library is required (Scan disabled otherwise); legacy no-body scan still works.
- All unit tests pass; `docker compose build api` and `docker compose build web` green; `WIZARD_ONLY=1 ./test-api.sh` and scoped-scan sandbox checks pass before `--prod-only`.

---

## 6. Verification / promotion (when approved)
`docker compose build api web` → `./scripts/promote.sh --sandbox-only` → manual/Playwright + shell verification in sandbox → `./scripts/promote.sh --prod-only`. Update ROADMAP items 343 & 426 to `[x]`, PROGRESS, and this plan's status line after promotion. Web hard refresh + verify served bundle hash.
