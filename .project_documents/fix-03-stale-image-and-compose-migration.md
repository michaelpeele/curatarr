# Fix #3: Stale Image & Compose Migration

**Status**: ✅ Complete (2026-11-08)  
**Priority**: 1 — Blocks all other operational work

---

## (a) Why the Wizard Kept Reappearing

The first-boot wizard reappeared on every container restart because the
SQLite database was written to a path the old manual containers did not
persist. The legacy `docker run` scripts mounted a named volume at `/app/data`
(one per host, e.g. `curatarr-data`), but the pre-Compose image ran with
`ASPNETCORE_CONTENTROOT` unset so the DB was created at the container's
writable layer (`/app`) and wiped on every restart. Meanwhile the Compose
stack points the content root at `/app/data` on the `app-data` volume — a
different volume than the one the manual containers used, so any DB that
did exist on `curatarr-data`/`curatarr_curatarr-data` was orphaned. A fresh
empty DB means `db.Users.AnyAsync()` is false, `GET /api/bootstrapped`
returns `{"isBootstrapped":false}`, and the UI renders the 4-step wizard
instead of the login screen.

---

## (b) Operator Runbook on the Pi

Run these commands **in exact order** from the project root:

```bash
cd /codebase/Curatarr

# 1. Remove manually-created containers (docker compose down will NOT stop these!)
docker rm -f curatarr-api curatarr-web ts-curatarr

# 2. Remove old host volumes (old DB NOT retained — user confirmed)
docker volume rm curatarr-data curatarr_curatarr-data

# 3. Remove stale images
docker rmi curatarr-api curatarr-web

# 4. Build and start compose-managed stack
docker compose up --build -d

# 5. Verify all services are running
docker compose ps

# 6. Verify fresh DB triggers wizard
curl http://localhost:5173/api/bootstrapped
# Expected: {"isBootstrapped":false}

# 7. Verify content root is correct
docker compose exec api env | grep ASPNETCORE_CONTENTROOT
# Expected: ASPNETCORE_CONTENTROOT=/app/data
```

### ⚠️ Important Notes

- **`docker compose down` alone will NOT stop the manually-created
  containers** — `curatarr-api`, `curatarr-web`, and `ts-curatarr` were
  created via `docker run` and are not tracked by Compose's project label.
  Step 1 must always run first.

- **Volume semantics**: The `app-data` volume (`curatarr-data`) is
  compose-managed and **auto-created** on first `docker compose up`.
  To reset the wizard later, remove the volume manually:
  `docker volume rm curatarr-data && docker compose up -d`.

- **Tailscale serve config**: `./curatarr-tailscale-data/serve.json` maps
  the shared-network-namespace `:80` (nginx in the `web` container) to the
  host-published port `5173`, using a plain HTTP proxy
  (`http://127.0.0.1:80`). No `${TS_CERT_DOMAIN}` or other placeholders —
  written cleanly for local HTTP operation. `TS_SERVE_CONFIG=
  /var/lib/tailscale/serve.json` resolves to this file via the bind mount
  `./${APP_NAME}-tailscale-data:/var/lib/tailscale` in `docker-compose.yml`.

---

## (c) Rollback Notes

**Old DB is NOT retained.** The migration deliberately discarded the
existing SQLite databases on `curatarr-data` and `curatarr_curatarr-data`.
Restoring means:

1. **Re-running the archived scripts** — `archive/manual-scripts/`
   contains `fix-deployment.sh` and `start-tailscale.sh`. They embed
   **rotated** Tailscale auth keys and hardcoded paths; both must be
   updated before use.
2. **Re-creating the stale containers** — recreate `curatarr-api` /
   `curatarr-web` / `ts-curatarr` via `docker run` with the legacy
   volume mounts.

**Recommendation**: rollback is not recommended. The compose stack is more
maintainable, and the user has confirmed the old DB is not required.

---

## Files Modified

| File | Change |
|------|--------|
| `docker-compose.yml` | Added header comment block (lines 1–8); `api.environment` has `Frontend__BaseUrl`; `api.healthcheck` is the real `wget` probe; `volumes.app-data` has no `external: true`; trailing volume NOTE comment kept |
| `curatarr-tailscale-data/serve.json` | Written cleanly: `{"Web":{"5173:80":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:80"}}}},"TCP":{},"AllowFunnel":{}}` |
| `archive/manual-scripts/fix-deployment.sh` | Moved from repo root (plain filesystem move; repo not a git repo) |
| `archive/manual-scripts/start-tailscale.sh` | Moved from repo root |
| `archive/manual-scripts/README.md` | New — deprecation notice; do-not-use; keys rotated (values not included) |
| `.project_documents/fix-03-stale-image-and-compose-migration.md` | This runbook (replaced prior "Ready for Implementation" draft) |
| `.project_documents/DOCKER_FIXES.md` | Created as index/checklist (Fix 3 done; 1, 4, 9, 10+11, scans-show-episodes pending; 2 & 12 skipped) |
| `CHECKPOINT.md` | Appended "Fix 3" section at end |
