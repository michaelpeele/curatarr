## Checkpoint: Curatarr — Folder-Level Symlinks (2026-11-14)

### Project Context
Project: Curatarr (AI-assisted TV/film collection manager)
Deployment: Docker on aarch64 host — `curatarr-api` (ASP.NET, :5000),
`curatarr-web` (nginx SPA, :5173), `ts-curatarr` (Tailscale sidecar)
Public URL: `https://curatarr.tail0307d.ts.net/`

### Session Goal
One poster card per show; one representative approve/unapprove row instead of
bulk approval; shows link their show folder in one symlink (flattened to the library
root — letter-group source containers dropped) so Plex/Jellyfin
walks seasons natively. Movies unchanged (per-file symlinks). No DB column migration.

### What Changed
| File | Change |
|------|--------|
| `Curatarr.Engine/SymlinkEngine.cs` | `ResolveLinkPaths`: Show/Anime → one dir symlink **flattened to library root** (file + season dirs stripped, ShowTitle segment authoritative); Movie → per-file. Collision-safe create branch. `IsSymlink` now detects dir symlinks too. |
| `Curatarr.Api/BulkApprovalEndpoints.cs` | **Deleted** — bulk endpoints removed entirely |
| `Curatarr.Api/Program.cs` | `MapBulkApprovalEndpoints()` unregistered. Approve made idempotent (skips if VLI already exists). Two new indexes seeded: `IX_SymlinkRecords_UniqueLinkPerLibrary`, `IX_VirtualLibraryItems_OnePerLibraryAndItem`. |
| `Curatarr.Web/src/lib/showGroups.ts` | `representativeId(group)` replaces bulk helpers. `episodeKey` path fallback. Season-0 excluded from `seasonCount`. |
| `Curatarr.Web/src/components/PosterGrid.tsx` | Single-row approve/unapprove via `api.approve`/`api.unapprove` on `representativeId`. Partial badge removed. `isApproved = approvedIds.has(representativeId)`. |
| `Curatarr.Web/src/api/client.ts` | `approveMany`/`unapproveMany` removed; `SourceItem.relativePath` added |
| `Curatarr.Web/src/components/DetailModal.tsx` | Show summary line preserved |

No database schema columns changed — existing per-episode SourceItem rows are used as metadata only.

### Verification
- [x] `tsc -b` clean; production Vite build (`dist/assets/index-D5tOK1C1.js`, 300 kB)
- [x] `curatarr-api:folder-level-symlinks` built
- [x] `curatarr-web:folder-level-symlinks` built
- [x] QA folder-symlink test: `/output/Bluey → /media/Shows/Bluey` created correctly
- [x] QA movie test: `/output/Arrival (2016)/Arrival.mkv` per-file symlink created
- [x] Double-approve idempotent: 3× HTTP 200 for same item, no unique-index 500
- [x] Re-reconcile idempotent: second call returns unchanged (no duplicate links)
- [x] Unapprove + reconcile: folder symlink removed cleanly
- [x] Collision safety: two episode rows for same show → single link, both recorded, no crash
- [x] Broken-source reporting: delete source file/dir → `Broken` status, no crash
- [ ] Browser visual confirmation (blocked — aarch64, no Chromium)

### Production State
- **Deployed 2026-11-14** via Step 0 gate under `bak-config/promotion/20261114-051028-folder-level-symlinks/`.
  Rollback images tagged `curatarr-api:pre-folder-symlinks` / `curatarr-web:pre-folder-symlinks`;
  online DB backup + restore-verification both passed (1 user, 1 library, 1877 source items,
  0 approved, 0 symlinks).
- `curatarr-api` now **healthy** after the healthcheck fix: `docker-compose.yml` healthcheck
  changed from `wget --spider` (HEAD → 405) to `curl -fs` (GET).
- `curatarr-web` serves `index-D5tOK1C1.js`; Tailscale Funnel active.
- Both new unique indexes confirmed live in the production DB.

### Next Steps
1. ✅ User approved production deployment (done 2026-11-14).
2. ✅ Step 0 backup/restore gate — done.
3. ✅ Deploy `api` + `web` images — done (tailscale sidecar untouched, serve.json intact).
4. ✅ Verify public URL — HTTP 200, API proxy 200, API healthy.
5. **Michael click-through** of the show cards + detail modal; then approve a show and confirm
   a folder symlink appears on disk under `/mnt/data/VirtualLibraries/`.
