# Docker Fixes Index

| Fix# | Title | Doc | Status | Summary |
|------|-------|-----|--------|---------|
| 3 | Stale Image & Compose Migration | [fix-03-stale-image-and-compose-migration.md](./fix-03-stale-image-and-compose-migration.md) | ✅ Done | Compose auto-created volume, real healthcheck, archiving manual scripts |
| 1 | TBD | — | ⏳ Pending | Sync `.env.example` with `docker-compose.yml` / `.env` keys — still desynced from compose (Fix 1 territory, untouched in Fix 3) |
| 2 | Skipped | — | ⏭️ Skipped | Intentionally skipped |
| 4 | TBD | — | ⏳ Pending | TBD |
| 9 | TBD | — | ⏳ Pending | TBD |
| 10+11 | Bundled with 5, 6, 7 | — | ⏳ Pending | Bundled with 5, 6, 7 |
| 12 | Skipped | — | ⏭️ Skipped | Intentionally skipped |
| scans-show-episodes | Scan shows episodes | — | ✅ Done | Scanner intentionally stores one `SourceItem` per episode file; UI groups those rows into one `ShowCard` per show (`src/lib/showGroups.ts`, `PosterGrid.tsx`). Show approval is a single representative row (`representativeId`); `SymlinkEngine` links the show folder in one symlink flattened to the library root (letter-group source containers dropped, e.g. `M thru Z/<Show>` → `/output/<Show>`; movies stay per-file). Verified 2026-11-14. |
| healthcheck-HEAD-vs-GET | API healthcheck used `wget --spider` (HEAD) against a GET-only endpoint → 405 → "unhealthy" | [docker-compose.yml](../docker-compose.yml) | ✅ Done 2026-11-14 | Healthcheck changed to `curl -fs http://localhost:5000/api/bootstrapped` (GET). Root cause: `--spider` sends HEAD, but `/api/bootstrapped` is `MapGet` only → `405 Method Not Allowed`, so the container misreported unhealthy while the API worked. Deployed + verified healthy 2026-11-14. |
