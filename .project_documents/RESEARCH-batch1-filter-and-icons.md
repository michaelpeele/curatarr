# Batch 1 — Media-type view filter + hide-per-library + card icons

Status: **analysis complete, frontend not yet implemented** (2026-11-17).
Backend is already built, migrated, deployed and curl-verified. This document is the single
reference for the remaining frontend work; it is linked from Sprint 4 #1 and Sprint 5 #6 in
`ROADMAP.md`.

## 1. What the user asked for

- A new first filter in the library grid, before **Genres**: `All <Type>`, `Library <Type>`,
  `Unmarked <Type>`, `Hidden <Type>`. Default `All`.
- Hidden state is **per library** and only affects that library.
- Pin the Mark/Hide actions to one fixed spot on every poster card (currently the text button
  drifts with title height).
- Replace the text buttons with state icons (keep tooltips and `aria-label`s):
  - **Mark:** gray check = not marked → yellow = marked but not yet synced → green = actually in
    the library after Sync.
  - **Hide:** gray eyeball = visible → yellow = hidden → eyeball with a red circle-slash when
    viewing the `Hidden <Type>` filter. Clicking it there unhides.

## 2. Backend already available (`Curatarr.Api/Program.cs`)

| Endpoint | Result | Access |
|---|---|---|
| `POST /api/virtual-libraries/{libraryId}/hidden-items` `{ sourceItemIds: string[] }` | `200 { hidden: number }` (total hidden for that library), idempotent | Admin bypass; Owner/Curator need a membership row on THAT library; Viewer 403 |
| `DELETE /api/virtual-libraries/{libraryId}/hidden-items` (same body); alias `POST .../unhide` | `200 { removed, hidden }` | same |
| `GET /api/virtual-libraries/{libraryId}/hidden-items` | `string[]` of source item ids | same |
| `GET /api/virtual-libraries/{libraryId}/approved-item-ids` | `string[]` | same |
| `GET /api/source-items?libraryId=X` | the 18 projected fields plus `Approved`, `ApprovedAt`, `Hidden` | authenticated; no `libraryId` → no annotations (deliberate, so Viewers can read flags) |

Schema (already migrated): `HiddenItem { Id, VirtualLibraryId, SourceItemId, CreatedAt }` with a
unique index on `(VirtualLibraryId, SourceItemId)`; `VirtualLibrary.LastReconciledAt`;
`VirtualLibraryItem.ApprovedAt`.

## 3. Files to change

### `Curatarr.Web/src/api/client.ts`
- `SourceItem` (lines 9-39): add optional `approved?: boolean`, `approvedAt?: string | null`,
  `hidden?: boolean`.
- `VirtualLibrary` (lines 41-46): add optional `lastReconciledAt?: string | null`.
- `listSourceItems()` (line 208): accept an optional `libraryId` and request
  `/api/source-items?libraryId=…`. It is used **only** by `PosterGrid.tsx` (5 call sites).
- Add `listHiddenItemIds`, `hideItems`, `unhideItems` methods using the endpoints above.
- Error display needs no work: `extractErrorMessage` already understands `{ error }`.

### `Curatarr.Web/src/components/App.tsx`
- Pass `lastReconciledAt={libraries.find(l => l.id === selectedLibraryId)?.lastReconciledAt ?? null}`
  to `PosterGrid` (render at lines 337-342; `libraries` line 261, `selectedLibraryId` line 263,
  `selectedLibMediaType` lines 266-269).

### `Curatarr.Web/src/components/PosterGrid.tsx` (~1312 lines)
- Props (line 90): add `lastReconciledAt?: string | null`.
- State: add `viewFilter: "all" | "library" | "unmarked" | "hidden"`, plus per-item hide
  bookkeeping. Reset `viewFilter` when `libraryId` changes; keep the existing reset on media-type
  change (lines 499-507).
- Load effect (327-334): call `api.listSourceItems(libraryId)` so `approved`/`approvedAt`/`hidden`
  come from the server; keep the existing `getApprovedItemIds` effect (336-354) and SignalR scan
  merge (360-407).
- `entries` memo (409-435): apply view semantics after the media-type filter and before the
  letter bucketing (759-765).
- Toolbar (770-906): insert a `SelectFilter` **first**, before Genres (~line 810). The generic
  `SelectFilter<T>` (199-264) can be reused with `T = string`; `value = null` means the default
  `All <Type>` option. Clear filters (879-893) must reset it too.
- Grid map (929-957): keep `LazyMount`, pass `markState` and hide props into both card types.
- `PosterCard` (ends 1138) and `ShowCard` (1140-1308): replace the text buttons at 1116-1134 and
  1286-1308 with a fixed-position icon row.

## 4. Filter semantics

`<Type>` = `Movies` / `TV Shows` / `Anime` from `libraryMediaType`, falling back to `Items`.

| View | Shows |
|---|---|
| `All <Type>` | the whole catalog **minus hidden** |
| `Library <Type>` | approved in the selected library, minus hidden |
| `Unmarked <Type>` | not approved, minus hidden |
| `Hidden <Type>` | hidden items only |

Chained genre/rating/studio/network/search filters continue to apply. For show groups: a group is
approved when its `representativeId` is approved, and hidden only when **all** of its
`episodeIds` are hidden. Hiding a show sends all `group.episodeIds`.

## 5. Icon state derivation

- `gray`: not approved.
- `yellow`: approved but `approvedAt` is null, or `approvedAt >= lastReconciledAt` (no Sync has
  completed since the mark).
- `green`: approved and `lastReconciledAt > approvedAt` (item is in the library after Sync).
- Hide: gray eyeball normally; yellow when hidden; in `Hidden <Type>` view hidden items get the
  eyeball with a red circle-slash overlay, and clicking toggles unhide.
- All icon buttons need `title` tooltips and `aria-label`s. Use optimistic updates with revert
  plus the existing toast mechanism on API failure.

## 6. Rollout (mandatory sandbox-first)

1. `docker compose build api web` once.
2. `./scripts/promote.sh --sandbox-only` — sandbox recreate, smoke, `WIZARD_ONLY=1 ./test-api.sh`.
3. Manual browser verification on `:5174` (filter switching, library switch reset, icon colours,
   hide/unhide round trip, mark → Sync → green).
4. `./scripts/promote.sh --prod-only` (or default `full` when no manual check is needed).
5. Hard refresh prod and verify the served bundle hash.

## 7. Verification checklist

- No page errors with 82,490 source items; `LazyMount` still keeps only a few posters mounted.
- All four views produce the expected sets on the Kids Movies library.
- Switching libraries resets the view filter to `All`.
- Hide → Sync keeps the item out of the library; unhide restores it.
- Mark → yellow immediately, green only after a Sync (check with `lastReconciledAt`).
- Viewer account gets 403 on hide/unhide but can still read grid flags.
- `docker compose build api` and `build web` both green.
