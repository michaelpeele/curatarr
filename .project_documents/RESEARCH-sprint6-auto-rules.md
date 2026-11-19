# Sprint 6 #1 — Automatic approve / hide rules

**Status:** research + spec complete (2026-11-17). Implementation next, sandbox-first.
**Roadmap:** `.project_documents/ROADMAP.md` → `## Sprint 6` item 1.
**Depends on (both Done):** Sprint 4 #1 (per-library hide) and Sprint 5 #8 (server-side paging/filtering).

## 1. What Curatarr does

Curatarr is a curation layer over a media catalog:

1. A scan discovers source titles (`SourceItem`) from source roots into a shared catalog.
2. Each `VirtualLibrary` has its own per-title decisions: **Marked** (`VirtualLibraryItem`) and **Hidden** (`HiddenItem`).
3. **Sync** runs `SymlinkEngine.ReconcileAsync`, writing/removing symlinks under the library's `OutputPath`, then nudges Plex with a path-scoped scan.
4. The grid exposes `All / Library / Unmarked / Hidden <Type>` plus Genre / Rating / Studio / Network / Search filters, now served by `/api/library-entries` and `/api/library-facets`.

Today every decision is manual, one card at a time. This item adds reusable rules that write the *same* state, so Sync, Plex refresh, icons and filters need no changes.

## 2. Use cases

- **Children's curated library:** a parent approves by rating (`G`, `PG`) and genre (`Animation`, `Family`) and hides `Horror`, `TV-MA`, etc. New titles are safe without hand-picking thousands of posters.
- **Adult overwhelmed by choices:** rules narrow the catalog by favourite director, actor, studio, network and rating. Manual overrides stay easy: mark one extra title, hide another.

## 3. What will be added

### Data model
`LibraryRule` (new, per `VirtualLibrary`):

| Field | Meaning |
|---|---|
| `Id` | string GUID |
| `VirtualLibraryId` | owning library |
| `Field` | one of `genre`, `director`, `cast`, `studio`, `network`, `rating`, `year`, `mediaType` |
| `Operator` | `equals`, `contains`, `in`, `gte`, `lte` (as appropriate for the field) |
| `Value` | comma/pipe-separated values as used by the existing filters |
| `Action` | `approve` or `hide` |
| `Priority` | int; lower runs first, later rules override earlier ones |
| `Enabled` | bool |
| `CreatedAt`, `UpdatedAt` | audit |

EF migration `AddLibraryRules`, unique index on `(VirtualLibraryId, Id)` is just the PK; no other uniqueness constraint (duplicate rules are allowed but the preview highlights overlaps).

### Generic field registry (shared by backend and frontend)
A single `RuleFields` list supplies `{ value, label, operators, kind }` for every filter field that exists now. New grid filters added later should register one entry here and automatically become rule fields — no new rule UI code.

### Backend
- `GET/POST/PUT/DELETE /api/virtual-libraries/{libraryId}/rules` — Admin bypass, Owner/Curator membership on THAT library, Viewer read-only or rejected (mirror the hidden-items endpoints).
- `POST /api/virtual-libraries/{libraryId}/rules/preview` with optional `ruleIds` → `{ toApprove: number, toHide: number, samples: string[] }`; evaluates rules without writing.
- `POST /api/virtual-libraries/{libraryId}/rules/apply` → `{ approved: number, hidden: number, unapproved: number, unhidden: number, logId }`; writes `VirtualLibraryItem` / `HiddenItem` rows and appends a `LibraryRuleRun` audit entry with counts and timestamp.
- Evaluation reuses `Curatarr.Engine/LibraryGrid/GridFilters.cs` predicates and the same token/delimiter semantics as `/api/library-entries`, so a rule preview matches what the grid filter shows.
- **Precedence:** manual decisions win over rules. Rules run in `Priority` order; a later rule can flip an earlier rule's outcome. When a rule sets `hide`, any `VirtualLibraryItem` for that title is removed — the physical symlink is removed on the next **Sync**, exactly like a manual unapprove. When a rule sets `approve`, the title is `HiddenItem`-removed and added to the library.
- Idempotent: a second Apply with unchanged rules reports zero changes.

### Frontend
- Settings → Libraries (or a per-library "Rules" tab) gets a per-library **Rules** panel: add rule (field / operator / value / action), list with enable/disable, reorder (Priority) and delete.
- **Preview** button shows the counts and a few sample titles before applying.
- **Apply** requires an explicit confirm; afterwards a toast reports the change counts and reminds that **Sync is required** for library removals to take effect on disk.
- Rule fields come from `/api/library-fields`, which is generated from the shared registry.

## 4. Guardrails / edge cases

- Preview must not mutate anything. Apply is audited and reversible only by editing rules + re-applying or by manual overrides.
- Empty value or unknown field/operator → 400 with an explicit message.
- Rules run against the library's media type only (a Movie library never evaluates Show fields).
- Applying rules does **not** trigger an immediate reconcile; the user still presses Sync. This keeps rule application cheap and predictable.
- Existing manual marks/hides must be preserved across unrelated rule runs; only titles matching a rule change.
- Run duration on 82k titles must be measured; if it exceeds a few seconds, move the apply to a background job with progress rather than blocking the request.

## 5. Verification / acceptance

1. `Curatarr.Tests` xUnit: field/operator evaluation parity with grid filters, precedence, manual override precedence, idempotence, media-type scoping, empty/invalid values.
2. Sandbox with Kids Movies: create `genre contains Family → approve`, `rating in TV-MA,R → hide`; preview counts; apply; confirm the grid's Library/Hidden views; Sync; confirm Plex paths.
3. No regression to S4 #1 / S5 #6 / S5 #8 behaviour.
4. Sandbox-first rollout per `/home/michaelpeele/ai-projects/workflow.md`: build once → `--sandbox-only` → manual verification → `--prod-only`.
