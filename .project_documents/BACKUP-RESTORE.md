# Curatarr — Backup and restore

Operator runbook for the SQLite database used by Curatarr. Use this before any
promotion, upgrade, restore, or blue/green cutover. The goal is a consistent
backup plus a verified restore on a scratch port before touching production.

All commands assume the repository root:

```bash
cd /home/michaelpeele/ai-projects/Curatarr
```

If you changed `APP_NAME`, replace `curatarr-data` and `curatarr-api` below with
your `${APP_NAME}-data` volume and `${APP_NAME}-api` container/image names.

## What is backed up

- SQLite database: `/app/data/curatarr.db` in the `curatarr-data` Docker volume.
- Compose config and `.env`.
- Tailscale serve state: `curatarr-tailscale-data/serve.json`.
- The current image IDs/tags, so a rollback target exists.

Media files are read-only inputs and are not backed up here. `/output` contains
generated symlink trees and can be rebuilt with Sync.

Do not commit backup folders. `bak-config/` is ignored by Git.

## 1. Create a backup folder

```bash
TS=$(date +%Y%m%d-%H%M%S)
BAK="bak-config/promotion/$TS-backup"
mkdir -p "$BAK/images"
```

## 2. Record rollback images and config

```bash
docker tag curatarr-api:latest curatarr-api:pre-$TS 2>/dev/null || \
  docker tag curatarr-api curatarr-api:pre-$TS
docker tag curatarr-web:latest curatarr-web:pre-$TS

docker image inspect curatarr-api:pre-$TS curatarr-web:pre-$TS \
  --format '{{.RepoTags}} {{.Id}}' | tee "$BAK/images/rollback-tags.txt"

cp docker-compose.yml .env "$BAK/"
cp curatarr-tailscale-data/serve.json "$BAK/serve.json" 2>/dev/null || true
docker compose ps > "$BAK/compose-ps-before.txt" 2>&1
docker exec ts-curatarr tailscale serve status > "$BAK/serve-status.txt" 2>&1 || true
```

## 3. Back up SQLite online

`sqlite3 .backup` creates a consistent snapshot while the API stays running.

```bash
docker run --rm \
  -v curatarr-data:/data \
  -v "$PWD/$BAK":/backup \
  alpine sh -c "apk add --no-cache sqlite >/dev/null && \
    sqlite3 /data/curatarr.db '.backup /backup/curatarr.db' && \
    (test -f /data/curatarr.db-wal && cp /data/curatarr.db-wal /backup/ || true)"

ls -lh "$BAK/curatarr.db"
```

WAL note: with `.backup`, restore from `curatarr.db` only. The copied
`curatarr.db-wal` is diagnostic evidence, not part of the restore set. If you
instead copy the raw `.db` file, stop the API first and copy `.db`, `.db-wal`,
and `.db-shm` together.

Record production row counts for comparison:

```bash
docker run --rm -v curatarr-data:/data alpine sh -c \
  "apk add --no-cache sqlite >/dev/null && \
   sqlite3 /data/curatarr.db \
   'select (select count(*) from AspNetUsers) as users, \
           (select count(*) from VirtualLibraries) as libraries, \
           (select count(*) from SourceItems) as items;'"
```

## 4. Verify the restore on a scratch volume

Never restore directly into production first.

```bash
docker volume create curatarr-restore-check >/dev/null

docker run --rm \
  -v curatarr-restore-check:/data \
  -v "$PWD/$BAK":/backup:ro \
  alpine sh -c "apk add --no-cache sqlite >/dev/null && \
    cp /backup/curatarr.db /data/curatarr.db && \
    chown -R 1654:1654 /data && \
    sqlite3 /data/curatarr.db 'pragma integrity_check;' && \
    sqlite3 /data/curatarr.db \
    'select (select count(*) from AspNetUsers) as users, \
            (select count(*) from VirtualLibraries) as libraries, \
            (select count(*) from SourceItems) as items;'"
```

The `chown` step matters: the API image runs as non-root uid 1654, so a
root-owned restored DB volume will fail with a permission error when the API
creates `/app/data/data-protection-keys`.

Boot the candidate API image against the restored copy:

```bash
docker run -d --name curatarr-restore-check --rm -p 15001:5000 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e MEDIA_ROOT=/media \
  -e OUTPUT_ROOT=/output \
  -v curatarr-restore-check:/app/data \
  curatarr-api:${CURATARR_VERSION:-latest}

curl -fsS http://localhost:15001/api/bootstrapped
# expect {"isBootstrapped":true}
```

Clean up:

```bash
docker rm -f curatarr-restore-check
docker volume rm curatarr-restore-check
```

**Gate:** do not continue until `integrity_check` returns `ok`, the row counts
match production, and the scratch API reports `isBootstrapped:true`.

## 5. Restore to production only if needed

This is destructive. Freeze writes by stopping the app first.

```bash
# Take a fresh backup of the current state before restoring.
docker compose stop api web

docker run --rm \
  -v curatarr-data:/app/data \
  -v "$PWD/$BAK":/backup:ro \
  alpine sh -c "cp /backup/curatarr.db /app/data/curatarr.db && \
    rm -f /app/data/curatarr.db-wal /app/data/curatarr.db-shm"

docker compose start api web
curl -fsS http://localhost:5000/api/bootstrapped
```

Log in and confirm libraries, users, and recent media changes are present. Do
not run a standby/blue stack against the same writable database at the same
time; see `PLAN-deployment-operations-hardening.md` for D4 blue/green rules.

## Promotion use

Before any promotion, complete sections 1–4 and keep the backup folder until
the new deployment has passed smoke tests. Rollback image tags are recorded in
`$BAK/images/rollback-tags.txt`.
