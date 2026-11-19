# Calendar slice 1 follow-up — "All libraries" UI flow

**Status:** implemented, sandbox-verified and promoted to prod on 2026-11-18. Keep Sync enabled with an explicit note; lock the Calendar media-type filter to the selected library's type.

## Question

If **All libraries** is selected on the Calendar page, there is no single library target for library-scoped actions. Should **Sync** be disabled on the Calendar page only, and what else should be disabled for a clean flow?

## Decision

- **Keep Sync enabled.** `/api/reconcile` is global and has no `libraryId`; disabling it would be a semantic change and would block applying slice 2 Mark/Hide choices.
- **Add a Calendar note:** "Sync applies to all libraries."
- **Lock the media-type filter** to the selected library's type as soon as a concrete library is selected. In All libraries view the filter remains user-selectable.
- Keep **Scan** and **Schedule** disabled in All libraries view. They are library-scoped and already behave correctly.
- Preserve the All libraries selection across Sync/library reloads.

## Short answer

- **Scan** and **Schedule**: keep disabled when All libraries is selected. This is already correct.
- **Sync**: **do not disable it just because All libraries is selected.** `/api/reconcile` has no `libraryId`; it intentionally reconciles all libraries. Disabling it would break the confirmed shared-toolbar behavior and the planned slice 2 flow.
- The real fixes are: show an explicit **All libraries** option in the bottom-bar selector, preserve the Calendar All selection across Sync/library reloads, clarify that bottom-bar Sync applies to all libraries, and lock the media-type filter to the library type when a single library is selected.
- In **slice 2**, an All-view entry must not guess a target library. Its modal should require a library choice and disable Mark/Hide until one is selected.

## Current behavior

| UI element | Current behavior with All libraries | Assessment |
| --- | --- | --- |
| Calendar library filter | `CalendarPage` calls `onSelectLibrary(null)`, which sets shared `selectedLibraryId` to `null`. | Correct intent, but the null state was not represented in the bottom bar and was reset after Sync. |
| Bottom-bar library selector | `value=""` with no matching `<option>`. | Bug/confusion: the control did not show "All libraries". |
| Scan | `disabled={scanning \|\| !selectedLibraryId}`, title "Select a library first". | Correct. `/api/scan` scopes roots by that library's `MediaType`; the UI deliberately requires one library. |
| Schedule | `disabled={!selectedLibraryId}`; modal guarded by `selectedLibraryId &&`. | Correct. Schedules are per library. |
| Schedule status label | Cleared when `selectedLibraryId` is null. | Correct. |
| Sync | Enabled; calls `api.reconcile()` → `POST /api/reconcile` with no body. | Correct. Reconcile is global by design. |
| Month arrows / date jump | Independent of library selection. | Keep enabled. |
| Media type filter | Followed `selectedLibMediaType`; with All libraries it reset to "All types" and remained editable with a concrete library. | Lock to library type when a concrete library is selected. |
| Status panels / toasts / Sign out | Global. | Keep enabled. |

## Hidden flow bug found

`App.tsx` reloads libraries when `librariesVersion` changes. `handleReconcile()` increments `librariesVersion`, then the reload effect does:

```ts
const stillExists = libs.some((l) => l.id === selectedLibraryId);
if (!stillExists) setSelectedLibraryId(libs.length > 0 ? libs[0].id : null);
```

When the user intentionally chose **All libraries**, `selectedLibraryId` is `null`, so `stillExists` is false and Sync silently switches the Calendar filter back to the first library. The same can happen after any libraries reload.

**Fix:** when the current view is `calendar` and `selectedLibraryId === null`, treat null as an intentional "All libraries" selection and do not auto-reselect the first library.

## UI rules

| Element | All libraries selected | Concrete library selected |
| --- | --- | --- |
| Calendar library filter | Enabled; shows `All libraries`. | Enabled; shows the library name. |
| Bottom-bar library selector | Shows an explicit `All libraries` option and keeps it selected. | Shows that library, matching the Calendar filter. |
| Scan | Disabled with title "Select a single library to scan". | Enabled for admins. |
| Sync | **Enabled**, with Calendar note "Sync applies to all libraries." | Enabled for admins, unchanged. |
| Schedule | Disabled with title "Select a single library to schedule". | Enabled for admins. |
| Month arrows / date jump | Enabled. | Enabled. |
| Media type filter | Enabled: All / Movie / Show / Anime. | Locked to the library's type, with an explanatory title. |
| Event cards | Read-only in slice 1. | Read-only in slice 1. |

## Why Sync stays enabled

1. `/api/reconcile` reconciles every library; it never asks for a target.
2. The shared toolbar must keep the library-page Sync behavior, as confirmed for slice 1.
3. Slice 2 will record Mark/Hide choices against libraries. Those choices need Sync to become real file/Plex changes. Disabling Sync in All view would force users to change filters before applying their choices, with no technical benefit.
4. The risk is copy, not capability: users may assume Sync follows the Calendar filter. A one-line note removes that ambiguity without changing behavior.

## Slice 2 implications

The planned All-view modal needs explicit target handling:

- If a concrete library is selected, preselect that library in the modal.
- If All libraries is selected, start with "Choose a library…" and keep **Mark for library** / **Hide from library** disabled until the user picks one.
- Reuse PosterGrid wording and the existing Sync warning verbatim.
- To make the choice easier, the Calendar API can later include each event's library memberships (ids/names). Slice 1 does not need them, but the All-view modal would benefit.

## Follow-up patch (slice 1 only)

1. `Curatarr.Web/src/App.tsx`
   - Preserve intentional `null` selection for `view === "calendar"` during the libraries reload effect.
   - Pass a flag to `BottomBar` indicating that the Calendar page allows an All-libraries selection.
2. `Curatarr.Web/src/components/BottomBar.tsx`
   - Widen `onSelectLibrary` to accept `string | null`.
   - Render `<option value="">All libraries</option>` when the Calendar flag is set.
   - Keep Scan/Schedule disabled when `selectedLibraryId` is null; improve titles.
3. `Curatarr.Web/src/components/CalendarPage.tsx`
   - Add the "Sync applies to all libraries" note.
   - Disable the media-type select when a concrete library is selected and set it to that library's type.
4. No API changes for this follow-up.

## Verification

Run the mandatory sandbox-first workflow after the patch:

- `docker compose build api web`.
- `scripts/promote.sh --sandbox-only`; wait for `:5001/api/bootstrapped`; smoke checks; `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
- Manual browser check on `:5174`: All libraries visible in both selectors, survives Sync, Scan/Schedule disabled, Sync enabled with note, media type locked to library type, month navigation still works.
- Record manual verification in `PROGRESS.md`, then `scripts/promote.sh --prod-only` if no code changes after verification.
