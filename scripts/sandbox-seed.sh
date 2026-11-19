#!/usr/bin/env bash
# ─── sandbox-seed.sh ────────────────────────────────────────────────────────
# Reset the sandbox DB volume (curatarr-sandbox-data) and seed it:
#   1. Base copy of the PROD DB (all tables, seed data, settings) -- copied
#      while the sandbox stack is DOWN, with the WAL included, and verified
#      with PRAGMA integrity_check before anything else touches it.
#   2. SourceItem detail columns (GenreIds, Rating, ProductionCompanies,
#      Networks) are then overwritten with the values currently in the
#      sandbox volume -- salvaged row-by-row first, because live copies of a
#      WAL-mode SQLite DB made while the API is running can be corrupt
#      (page-level corruption from a stale WAL).
#   3. Ownership is fixed to uid 1654 (the non-root user the api image runs
#      as -- the volume would otherwise be "readonly" to the app).
#   4. Optional demo backfill work: `--null-ratings N` sets Rating = NULL on
#      N random rows so the filter status UI ("Loading... x / y" ->
#      "✓ Completed") can be exercised against the sandbox.
#
# Usage (from the repo root):
#   ./scripts/sandbox-seed.sh                      # prod base + keep sandbox details
#   ./scripts/sandbox-seed.sh --null-ratings 1500  # ...plus demo backfill work
#   ./scripts/sandbox-seed.sh --no-salvage         # prod base, prod details (fresh clone)
#
# After it finishes:
#   docker compose -f docker-compose.sandbox.yml up -d
#   BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh
#
# Safe by design: the prod volume is copied out but never modified (the
# script only ever writes to curatarr-sandbox-data). Requires the sandbox
# stack to be down; the script stops it first.
set -euo pipefail

NULL_RATINGS=0
SALVAGE=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --null-ratings) NULL_RATINGS="$2"; shift 2 ;;
    --no-salvage)   SALVAGE=0; shift ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

PROD_VOL=curatarr-data
SBX_VOL=curatarr-sandbox-data
WORK=$(mktemp -d /tmp/opencode/sbx-seed.XXXXXX)
trap 'rm -rf "$WORK"' EXIT

echo "==> stopping sandbox stack (if running)"
docker compose -f docker-compose.sandbox.yml down 2>/dev/null || true

echo "==> copying prod DB (db + wal + shm) for use as the base"
docker run --rm -v "$PROD_VOL":/prod -v "$WORK":/out python:3.11-slim \
  sh -c "cp /prod/curatarr.db /prod/curatarr.db-wal /prod/curatarr.db-shm /out/ 2>/dev/null; ls /out"

echo "==> verifying prod copy integrity"
docker run --rm -v "$WORK":/out python:3.11-slim python3 -c "
import sqlite3
c = sqlite3.connect('/out/curatarr.db')
assert c.execute('PRAGMA integrity_check').fetchone()[0] == 'ok', 'prod copy is corrupt'
print('   prod copy OK')
"

if [[ "$SALVAGE" == "1" ]]; then
  echo "==> salvaging sandbox SourceItem details (row-by-row; tolerates corruption)"
  # The sandbox volume may hold a corrupt WAL copy. Read every row by
  # rowid -- single-row reads of intact pages succeed even when the overall
  # table scan fails.
  docker run --rm -v "$SBX_VOL":/data -v "$WORK":/out python:3.11-slim python3 -c "
import sqlite3, json, os
if not os.path.exists('/data/curatarr.db'):
    print('   no sandbox DB -- skipping salvage (fresh volume)')
    raise SystemExit(0)
src = sqlite3.connect('file:/data/curatarr.db?immutable=1', uri=True)
rows = {}
for rid in range(1, 200000):
    try:
        row = src.execute('SELECT Id, GenreIds, Rating, ProductionCompanies, Networks FROM SourceItems WHERE rowid=?', (rid,)).fetchone()
    except Exception:
        continue
    if row is None:
        continue
    rows[row[0]] = row  # keep Id so the apply step can key on it
with open('/out/salvage.jsonl', 'w') as f:
    for r in rows.values():
        json.dump(r, f); f.write('\n')
print('   salvaged', len(rows), 'rows')
"
fi

echo "==> building fresh sandbox DB: prod base + sandbox details"
if [[ "$SALVAGE" == "1" ]]; then
  docker run --rm -v "$SBX_VOL":/data -v "$WORK":/out python:3.11-slim python3 -c "
import sqlite3, json, shutil, os
shutil.copy('/out/curatarr.db', '/data/curatarr.db')
c = sqlite3.connect('/data/curatarr.db')
c.execute('PRAGMA journal_mode=WAL')
if os.path.exists('/out/salvage.jsonl'):
    rows = []
    with open('/out/salvage.jsonl') as f:
        for line in f:
            r = json.loads(line)
            # r = (Id, GenreIds, Rating, ProductionCompanies, Networks)
            rows.append((r[1], r[2], r[3], r[4], r[0]))
    print('   applying', len(rows), 'salvaged rows')
    c.executemany('UPDATE SourceItems SET GenreIds=?, Rating=?, ProductionCompanies=?, Networks=? WHERE Id=?', rows)
    c.commit()
    print('   details applied:', c.total_changes)
"
else
  docker run --rm -v "$SBX_VOL":/data -v "$WORK":/out python:3.11-slim sh -c \
    "cp /out/curatarr.db /data/curatarr.db"
fi

echo "==> fixing ownership (app runs as uid 1654)"
docker run --rm -v "$SBX_VOL":/data python:3.11-slim chown -R 1654:1654 /data

if [[ "$NULL_RATINGS" -gt 0 ]]; then
  echo "==> nulling Rating on $NULL_RATINGS random rows (demo backfill work)"
  docker run --rm -v "$SBX_VOL":/data python:3.11-slim python3 -c "
import sqlite3
c = sqlite3.connect('/data/curatarr.db')
cur = c.execute(\"UPDATE SourceItems SET Rating = NULL WHERE id IN (SELECT id FROM SourceItems WHERE Rating IS NOT NULL ORDER BY RANDOM() LIMIT $NULL_RATINGS)\")
c.commit()
print('   nulled', cur.rowcount)
"
fi

echo "==> final integrity check"
docker run --rm -v "$SBX_VOL":/data python:3.11-slim python3 -c "
import sqlite3
c = sqlite3.connect('file:/data/curatarr.db?immutable=1', uri=True)
assert c.execute('PRAGMA integrity_check').fetchone()[0] == 'ok', 'sandbox DB is corrupt'
n = c.execute('SELECT COUNT(*) FROM SourceItems').fetchone()[0]
print('   sandbox DB OK --', n, 'SourceItems')
"

echo "==> done. Start the sandbox with:"
echo "    docker compose -f docker-compose.sandbox.yml up -d"