# Promotion Runbook — Folder-Level Symlinks

**Status:** ⏸ Awaiting explicit user approval (Production Deployment Rule §9)
**Candidate images:** `curatarr-api:folder-level-symlinks`, `curatarr-web:folder-level-symlinks`
**Live images being replaced:** `curatarr-api` (digest `a36ba66…`), `curatarr-web:latest` (digest `1b01c41…`)
**Live URL:** `https://curatarr.tail0307d.ts.net/` (Tailscale Funnel → `127.0.0.1:80` in `ts-curatarr`)

## 0. Governance gate (visible)

```
PRODUCTION CHANGE GATE
  [x] Sandbox-first work complete (build + HTTP QA + folder-symlink QA + collision/idempotency checks)
  [ ] User approval recorded
  [ ] Step 0 backup/restore gate visible (below)
  [x] /mnt/data mount verified: nfs4 from 192.168.1.18, 8.1T free (92% used)
  [~] Resource gate: 63 MiB "free", 3.3 GiB "available", swap 5.0/8.0 GiB used
```

> The literal `free` column is low because Linux uses spare RAM as page cache; `available`
> is the meaningful number. Builds are already complete, so deployment only restarts two
> containers. Recorded for transparency; flag if you want a stricter reading of §7.1.

## Step 0 — Backup / restore gate

Set a timestamp and create the promotion folder:

```bash
cd /home/michaelpeele/ai-projects/Curatarr
TS=$(date +%Y%m%d-%H%M%S)
BAK="bak-config/promotion/$TS-folder-level-symlinks"
mkdir -p "$BAK/images"
```

### 0.1 Rollback image tags + digests

```bash
docker tag curatarr-api        curatarr-api:pre-folder-symlinks
docker tag curatarr-web:latest curatarr-web:pre-folder-symlinks
docker image inspect curatarr-api:pre-folder-symlinks curatarr-web:pre-folder-symlinks \
  --format '{{.RepoTags}} {{.Id}}' | tee "$BAK/images/rollback-tags.txt"
```

### 0.2 Config + Tailscale serve state

```bash
cp docker-compose.yml .env "$BAK/"
cp curatarr-tailscale-data/serve.json "$BAK/serve.json"
docker exec ts-curatarr tailscale serve status > "$BAK/serve-status.txt" 2>&1
docker compose ps > "$BAK/compose-ps-before.txt" 2>&1
```

### 0.3 SQLite online backup (no API stop needed)

```bash
docker run --rm \
  -v curatarr-data:/data \
  -v "$PWD/$BAK":/backup \
  alpine sh -c "apk add --no-cache sqlite >/dev/null && \
    sqlite3 /data/curatarr.db '.backup /backup/curatarr.db' && \
    (test -f /data/curatarr.db-wal && cp /data/curatarr.db-wal /backup/ || true)"
ls -lh "$BAK/curatarr.db"
```

### 0.4 Restore verification (scratch volume, never touches prod)

```bash
docker volume create curatarr-restore-check >/dev/null
docker run --rm -v curatarr-restore-check:/data -v "$PWD/$BAK":/backup:ro \
  alpine sh -c 'cp /backup/curatarr.db /data/curatarr.db && ls -l /data'
docker run --rm -v curatarr-restore-check:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null && \
    sqlite3 /data/curatarr.db 'select (select count(*) from AspNetUsers) as users, (select count(*) from VirtualLibraries) as libraries, (select count(*) from SourceItems) as items;'"
# Boot the NEW api image against the restored DB on a scratch port
docker run -d --name curatarr-restore-check --rm -p 15001:5000 \
  -v curatarr-restore-check:/app/data curatarr-api:folder-level-symlinks
sleep 8
curl -fsS http://localhost:15001/api/bootstrapped   # expect {"isBootstrapped":true}
docker rm -f curatarr-restore-check
docker volume rm curatarr-restore-check
```

**Gate: do not proceed until 0.4 prints the same counts as production and
`isBootstrapped:true`.**

## Deploy

```bash
docker tag curatarr-api:folder-level-symlinks curatarr-api:latest
docker tag curatarr-web:folder-level-symlinks curatarr-web:latest
docker compose up -d --no-build api web
# If compose reports "up-to-date" (image-ID label not refreshed), force it:
# docker compose up -d --no-build --force-recreate api web
```

`web` uses `network_mode: service:tailscale`; recreating it does not touch the Tailscale
sidecar or `serve.json`. Re-apply serve config only if `tailscale serve status` changes:

```bash
docker exec ts-curatarr tailscale serve status
```

## Verify

```bash
docker compose ps                                    # api healthy, web up
curl -fsS http://localhost:5000/api/bootstrapped     # {"isBootstrapped":true}
curl -fsS http://localhost:5173/ | head -5           # SPA shell
curl -fsS https://curatarr.tail0307d.ts.net/ | head -5
curl -fsS -o /dev/null -w '%{http_code}\n' \
  -X POST https://curatarr.tail0307d.ts.net/api/virtual-libraries/x/approve \
  -H 'Content-Type: application/json' -d '{"sourceItemId":"test"}'   # expect 401 (auth required)
```

Browser click-through: exactly one card per show, Mark/Unmark works (all-or-nothing, no
partial badge), movies unchanged. Then check the symlink tree on disk:
`find /mnt/data/VirtualLibraries/<library> -type l` should show show folder symlinks
(e.g. `Bluey -> /media/Shows/Bluey`) and movie file symlinks (`Arrival (2016)/Arrival.mkv`).

## Rollback (any failure)

```bash
docker tag curatarr-api:pre-folder-symlinks curatarr-api:latest
docker tag curatarr-web:pre-folder-symlinks curatarr-web:latest
docker compose up -d --no-build api web
docker cp "$BAK/serve.json" ts-curatarr:/var/lib/tailscale/serve.json
docker exec ts-curatarr tailscale serve reset   # only if serve config was changed
```

No DB column migration was introduced (only two unique indexes, which are backward-compatible
no-ops on an empty DB). No DB rollback is required for this feature.
