#!/usr/bin/env bash
# ─── promote.sh ───────────────────────────────────────────────────────────────
# Sandbox-first build, verification and (when green) auto-promotion to prod.
# Canonical rules: /home/michaelpeele/ai-projects/workflow.md
#
# Usage (from anywhere):
#   ./scripts/promote.sh                 # full: build -> sandbox check -> auto-promote prod
#   ./scripts/promote.sh --sandbox-only  # build + sandbox check, stop for manual web testing
#   ./scripts/promote.sh --prod-only     # deploy already-verified images after manual checks
#   ./scripts/promote.sh --seed          # full, but reseed the sandbox DB from prod first
#
# Optional env: ADMIN_EMAIL, ADMIN_PASSWORD (defaults to the documented local admin).
set -Eeuo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/promote.sh [mode] [options]

Existing promotion modes:
  (no mode)                  Full: build -> sandbox check -> auto-promote prod.
  --sandbox-only             Build + sandbox check; stop for manual web testing.
  --prod-only                Deploy already-verified images after manual checks.
  --seed                     Full, but reseed the sandbox DB from prod first.

D4 blue/green modes (read-only unless stated):
  --status                   Show active/standby slots, container health and
                             the edge proxy upstream. No state is changed.
  --deploy-standby           Recreate only the standby slot from verified images.
                             The active slot and proxy routing are unchanged.
  --smoke-standby            Smoke the standby API/web ports and verify its DB.
  --flip --confirm-cutover   Verify the refreshed standby, then switch the edge
                             proxy to it. Requires an operator write freeze and
                             a final snapshot (BACKUP_DB, optional BACKUP_KEYS_DIR).
  --rollback --confirm-cutover
                             Switch the edge proxy back to the previous slot.
                             Both modes set the target slot's /output mounts rw
                             and the old active slot ro after the proxy switch.

Options:
  --check                    With --deploy-standby only: validate configuration
                             and report the target without starting containers.
  --confirm-cutover          Mandatory explicit consent for --flip/--rollback.
  -h, --help                 Show this help.

D4 env overrides: BLUE_ENV_FILE, GREEN_ENV_FILE, EDGE_ENV_FILE,
EDGE_TEMPLATE_FILE, EDGE_STATE_DIR, BACKUP_DB, BACKUP_KEYS_DIR. Real slot env
files default to configs/slot-<blue|green>.env.
Slot /output mounts default to ro via OUTPUT_MOUNT_MODE; D4 modes set rw only on
the active slot. Proxy-facing slots must keep COOKIE_NAME=curatarr.auth.

Cutover is a separate, user-approved operation. This script never changes
production routing without --confirm-cutover.
EOF
}

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

MODE=full
SEED=0
CONFIRM_CUTOVER=0
CHECK_ONLY=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --sandbox-only|--prod-only|--status|--deploy-standby|--smoke-standby|--flip|--rollback)
      MODE="${1#--}" ;;
    --seed) SEED=1 ;;
    --confirm-cutover) CONFIRM_CUTOVER=1 ;;
    --check) CHECK_ONLY=1 ;;
    -h|--help)
      usage
      exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

if [[ $CHECK_ONLY -eq 1 && "$MODE" != deploy-standby ]]; then
  echo "--check is only valid with --deploy-standby" >&2
  exit 2
fi
if [[ $CONFIRM_CUTOVER -eq 1 && "$MODE" != flip && "$MODE" != rollback ]]; then
  echo "--confirm-cutover is only valid with --flip or --rollback" >&2
  exit 2
fi

SANDBOX_COMPOSE=docker-compose.sandbox.yml
SLOT_COMPOSE="$ROOT/docker-compose.slot.yml"
EDGE_SH="$ROOT/scripts/edge.sh"
ACTIVE_FILE="$ROOT/state/active-slot"
LOG_FILE="$ROOT/logs/promotions.log"
PUBLIC_COOKIE_NAME=curatarr.auth
mkdir -p "$(dirname "$LOG_FILE")"

log() { echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] [$MODE] $*" | tee -a "$LOG_FILE"; }
on_error() {
  local rc=$?
  trap - ERR
  log "FAILED (exit $rc) at line $1 — prod was not promoted from this run."
  exit "$rc"
}
trap 'on_error $LINENO' ERR

fail() { log "ERROR: $*"; echo "$*" >&2; return 1; }

wait_ready() { # wait_ready <url> <label>
  local url="$1" label="$2" code=""
  for _ in $(seq 1 60); do
    code=$(curl -s -o /dev/null -w '%{http_code}' "$url" || true)
    [[ "$code" == "200" ]] && return 0
    sleep 2
  done
  echo "$label did not become ready ($url returned ${code:-no response})" >&2
  return 1
}

smoke() { # smoke <base-url> <label>
  local base="$1" label="$2" jar
  jar=$(mktemp)
  trap 'rm -f "${jar:-}"' RETURN
  wait_ready "$base/api/bootstrapped" "$label"

  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' -c "$jar" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"${ADMIN_EMAIL:-michael.peele@gmail.com}\",\"password\":\"${ADMIN_PASSWORD:-Passw0rd!123}\"}" \
    "$base/api/auth/login")
  [[ "$code" == "200" ]] || { echo "$label login failed (HTTP $code)" >&2; return 1; }

  for path in /api/source-items /api/virtual-libraries /api/me; do
    code=$(curl -s -o /dev/null -w '%{http_code}' -b "$jar" "$base$path")
    [[ "$code" == "200" ]] || { echo "$label smoke $path failed (HTTP $code)" >&2; return 1; }
  done
  echo "$label smoke: OK"
}

build_images() {
  log "Building api + web images from the shared tree..."
  docker compose build api web
  log "Build complete."
}

verify_sandbox() {
  log "Recreating sandbox (:5001/:5174) from the new images..."
  docker compose -f "$SANDBOX_COMPOSE" up -d --force-recreate --no-build

  if [[ $SEED -eq 1 ]]; then
    log "Reseeding sandbox DB from prod (sandbox stack stopped)..."
    docker compose -f "$SANDBOX_COMPOSE" down
    ./scripts/sandbox-seed.sh --null-ratings 1500
    docker compose -f "$SANDBOX_COMPOSE" up -d --no-build
  fi

  wait_ready http://localhost:5001/api/bootstrapped "sandbox API"
  smoke http://localhost:5001 sandbox

  log "Running zero-write guard tests against sandbox..."
  BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh

  log "Sandbox verification PASSED."
}

promote_prod() {
  log "Promoting verified images to prod (:5000/:5173)..."
  docker compose up -d --no-build api web
  smoke http://localhost:5000 prod
  log "Prod deployment and smoke checks PASSED. Hard-refresh the browser to pick up the new web bundle."
}

# ─── D4 blue/green helpers ────────────────────────────────────────────────────

slot_env_file() { # slot_env_file <blue|green>
  local slot="$1" override=""
  case "$slot" in
    blue) override="${BLUE_ENV_FILE:-}" ;;
    green) override="${GREEN_ENV_FILE:-}" ;;
    *) return 1 ;;
  esac
  if [[ -n "$override" ]]; then
    echo "$override"
  elif [[ -f "$ROOT/configs/slot-$slot.env" ]]; then
    echo "$ROOT/configs/slot-$slot.env"
  else
    echo "$ROOT/configs/slot-$slot.env.example"
  fi
}

d4_active_slot() {
  local slot
  [[ -f "$EDGE_SH" ]] || fail "missing $EDGE_SH"
  slot=$("$EDGE_SH" status | sed -n 's/^active_slot=//p')
  case "$slot" in
    blue|green) echo "$slot" ;;
    *) fail "could not read active slot from '$EDGE_SH status'" ;;
  esac
}

d4_standby_slot() {
  case "$1" in
    blue) echo green ;;
    green) echo blue ;;
    *) return 1 ;;
  esac
}

edge_env_file() {
  if [[ -n "${EDGE_ENV_FILE:-}" && -f "${EDGE_ENV_FILE:-}" ]]; then
    echo "$EDGE_ENV_FILE"
  elif [[ -f "$ROOT/configs/edge.env" ]]; then
    echo "$ROOT/configs/edge.env"
  else
    echo "$ROOT/configs/edge.env.example"
  fi
}

edge_domain() {
  local env_file domain
  env_file=$(edge_env_file)
  domain=$(
    set -a
    # shellcheck disable=SC1090
    . "$env_file"
    printf '%s' "${CURATARR_DOMAIN:-}"
  )
  [[ -n "$domain" ]] || fail "CURATARR_DOMAIN is not set in $env_file"
  echo "$domain"
}

require_confirm_cutover() {
  [[ $CONFIRM_CUTOVER -eq 1 ]] || \
    fail "$1 requires --confirm-cutover; production routing was not changed"
}

PY_DB_COUNTS='
import sqlite3, sys
c = sqlite3.connect("file:" + sys.argv[1] + "?mode=ro", uri=True)
print("integrity_check=" + str(c.execute("PRAGMA integrity_check").fetchone()[0]))
for t in ("AspNetUsers", "VirtualLibraries", "SourceItems"):
    print(t + "=" + str(c.execute("SELECT COUNT(*) FROM " + t).fetchone()[0]))
'

verify_slot_db() { # verify_slot_db <slot> [backup-db]
  local slot="$1"
  local backup_db="${2:-}"
  local vol="curatarr-${slot}-data"
  local live backup
  log "Verifying DB for slot $slot (volume $vol)..."
  live=$(docker run --rm -v "$vol:/data" python:3.11-slim \
    python3 -c "$PY_DB_COUNTS" /data/curatarr.db) || fail "DB check failed for slot $slot"
  echo "$live"
  if [[ -n "$backup_db" ]]; then
    [[ -f "$backup_db" ]] || fail "backup DB not found: $backup_db"
    backup=$(docker run --rm -v "$backup_db:/backup/curatarr.db:ro" python:3.11-slim \
      python3 -c "$PY_DB_COUNTS" /backup/curatarr.db) || fail "backup DB check failed: $backup_db"
    echo "backup: $backup"
    [[ "$live" == "$backup" ]] || fail "slot $slot row counts differ from $backup_db"
  fi
  log "DB verification PASSED for slot $slot."
}

slot_api_port() { # slot_api_port <env-file>
  sed -n 's/^API_LOCAL_PORT=//p' "$1" | tail -n 1
}

slot_web_port() { # slot_web_port <env-file>
  sed -n 's/^WEB_LOCAL_PORT=//p' "$1" | tail -n 1
}

slot_expected_output_mode() { # slot_expected_output_mode <slot> <active>
  local slot="$1" active="$2"
  case "$slot" in
    "$active") echo rw ;;
    *) echo ro ;;
  esac
}

set_slot_output_mode() { # set_slot_output_mode <slot> <rw|ro>
  local slot="$1" mode="$2" env_file
  case "$mode" in
    rw|ro) ;;
    *) fail "invalid output mount mode '$mode' for slot $slot" ;;
  esac
  env_file=$(slot_env_file "$slot")
  [[ -f "$env_file" ]] || fail "missing env file for slot $slot: $env_file"
  log "Setting output mount mode for slot $slot to $mode..."
  OUTPUT_MOUNT_MODE="$mode" docker compose -f "$SLOT_COMPOSE" --env-file "$env_file" up -d --force-recreate --no-build api web
  log "Output mount mode for slot $slot is now $mode."
}

public_cookie_name() { # public_cookie_name <env-file>
  local env_file="$1" name=""
  [[ -f "$env_file" ]] || fail "missing env file: $env_file"
  if [[ -n "${COOKIE_NAME:-}" ]]; then
    name="$COOKIE_NAME"
  else
    name=$(sed -n 's/^COOKIE_NAME=//p' "$env_file" | tail -n 1)
  fi
  printf '%s' "${name:-$PUBLIC_COOKIE_NAME}"
}

require_public_cookie() { # require_public_cookie <env-file> <slot>
  local env_file="$1" slot="$2" name
  name=$(public_cookie_name "$env_file")
  if [[ "$name" != "$PUBLIC_COOKIE_NAME" ]]; then
    fail "slot $slot must use COOKIE_NAME=$PUBLIC_COOKIE_NAME for proxy-facing traffic (found $name)"
  fi
  log "Public cookie check PASSED for slot $slot (COOKIE_NAME=$name)."
}

d4_status() {
  local active standby slot env_file expected_mode cookie_name have_docker=0
  active=$(d4_active_slot)
  standby=$(d4_standby_slot "$active")
  log "active_slot=$active standby_slot=$standby"
  echo "active_slot=$active"
  echo "standby_slot=$standby"
  if command -v docker >/dev/null 2>&1; then have_docker=1; fi
  [[ $have_docker -eq 1 ]] || log "docker CLI unavailable; skipped container health"
  for slot in "$active" "$standby"; do
    env_file=$(slot_env_file "$slot")
    expected_mode=$(slot_expected_output_mode "$slot" "$active")
    cookie_name=$(public_cookie_name "$env_file")
    log "slot=$slot env_file=$env_file expected_output_mode=$expected_mode cookie_name=$cookie_name"
    echo "--- slot $slot (env: $env_file, output: $expected_mode, cookie: $cookie_name) ---"
    if [[ $have_docker -eq 1 ]]; then
      docker compose -f "$SLOT_COMPOSE" --env-file "$env_file" ps 2>&1 || true
    else
      echo "(docker CLI unavailable; skipped container health)"
    fi
  done
  echo "--- edge proxy ---"
  "$EDGE_SH" status || true
  log "STATUS complete: active_slot=$active standby_slot=$standby"
}

d4_deploy_standby() {
  local active standby env_file mode=ro
  active=$(d4_active_slot)
  standby=$(d4_standby_slot "$active")
  env_file=$(slot_env_file "$standby")
  require_public_cookie "$env_file" "$standby"
  log "active_slot=$active standby_slot=$standby env_file=$env_file output_mode=$mode"
  [[ "$env_file" == *.example ]] && \
    { [[ $CHECK_ONLY -eq 1 ]] || fail "create $ROOT/configs/slot-$standby.env before deploying"; }
  OUTPUT_MOUNT_MODE="$mode" docker compose -f "$SLOT_COMPOSE" --env-file "$env_file" config --quiet || \
    fail "standby slot compose config is invalid ($env_file)"
  if [[ $CHECK_ONLY -eq 1 ]]; then
    log "deploy-check OK: active_slot=$active standby_slot=$standby output_mode=$mode (no containers started)"
    echo "deploy-check OK: active=$active standby=$standby output_mode=$mode"
    return 0
  fi
  log "Recreating standby slot $standby from verified images with output_mode=$mode..."
  OUTPUT_MOUNT_MODE="$mode" docker compose -f "$SLOT_COMPOSE" --env-file "$env_file" \
    up -d --force-recreate --no-build api web
  log "DEPLOY-STANDBY complete: active_slot=$active standby_slot=$standby output_mode=$mode (routing unchanged)"
}

d4_smoke_standby() {
  local active standby env_file api_port web_port expected_mode="${1:-}"
  active=$(d4_active_slot)
  standby=$(d4_standby_slot "$active")
  if [[ -z "$expected_mode" ]]; then
    expected_mode=$(slot_expected_output_mode "$standby" "$active")
  fi
  case "$expected_mode" in
    rw|ro) ;;
    *) fail "invalid expected output mount mode '$expected_mode' for slot $standby" ;;
  esac
  env_file=$(slot_env_file "$standby")
  [[ -f "$env_file" ]] || fail "missing env file for standby slot $standby: $env_file"
  require_public_cookie "$env_file" "$standby"
  api_port=$(slot_api_port "$env_file")
  web_port=$(slot_web_port "$env_file")
  [[ -n "$api_port" && -n "$web_port" ]] || fail "API_LOCAL_PORT/WEB_LOCAL_PORT missing in $env_file"
  log "active_slot=$active standby_slot=$standby api_port=$api_port web_port=$web_port output_mode=$expected_mode"
  wait_ready "http://127.0.0.1:$web_port" "standby web $standby"
  smoke "http://127.0.0.1:$api_port" "standby API $standby"
  verify_slot_db "$standby" "${BACKUP_DB:-}"
  log "SMOKE-STANDBY complete: active_slot=$active standby_slot=$standby output_mode=$expected_mode"
}

restore_slot_db() { # restore_slot_db <slot> <backup-db> [backup-keys-dir]
  local slot="$1"
  local backup_db="$2"
  local keys_dir="${3:-}"
  local vol="curatarr-${slot}-data"
  [[ -f "$backup_db" ]] || fail "backup DB not found: $backup_db"
  log "Restoring final snapshot into standby slot $slot (volume $vol)..."
  docker run --rm -v "$vol:/data" -v "$backup_db:/backup/curatarr.db:ro" python:3.11-slim sh -c \
    "cp /backup/curatarr.db /data/curatarr.db && \
     rm -f /data/curatarr.db-wal /data/curatarr.db-shm && \
     chown -R 1654:1654 /data"
  if [[ -n "$keys_dir" ]]; then
    [[ -d "$keys_dir" ]] || fail "data-protection-keys directory not found: $keys_dir"
    docker run --rm -v "$vol:/data" -v "$keys_dir:/keys:ro" python:3.11-slim sh -c \
      "mkdir -p /data/data-protection-keys && cp -a /keys/. /data/data-protection-keys/ && \
       chown -R 1654:1654 /data"
  fi
  verify_slot_db "$slot" "$backup_db"
}

smoke_public_url() { # smoke_public_url <domain-or-url>
  local domain="$1" base jar code
  local curl_secure=()
  if [[ "$domain" == http://* || "$domain" == https://* ]]; then
    base="$domain"
  else
    base="https://$domain"
  fi
  [[ "$base" == https://* ]] && curl_secure=(-k)
  jar=$(mktemp)
  trap 'rm -f "${jar:-}"' RETURN
  code=$(curl -sS "${curl_secure[@]}" -o /dev/null -w '%{http_code}' -c "$jar" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"${ADMIN_EMAIL:-michael.peele@gmail.com}\",\"password\":\"${ADMIN_PASSWORD:-Passw0rd!123}\"}" \
    "$base/api/auth/login")
  [[ "$code" == "200" ]] || fail "public URL login failed (HTTP $code)"
  for path in /api/source-items /api/virtual-libraries /api/me; do
    code=$(curl -sS "${curl_secure[@]}" -o /dev/null -w '%{http_code}' -b "$jar" "$base$path")
    [[ "$code" == "200" ]] || fail "public URL smoke $path failed (HTTP $code)"
  done
  log "public URL smoke PASSED for $base"
}

d4_flip() {
  require_confirm_cutover flip
  local active standby active_env_file standby_env_file domain backup_db keys_dir
  active=$(d4_active_slot)
  standby=$(d4_standby_slot "$active")
  active_env_file=$(slot_env_file "$active")
  standby_env_file=$(slot_env_file "$standby")
  [[ "$active_env_file" == *.example ]] && fail "create $ROOT/configs/slot-$active.env before flipping"
  [[ "$standby_env_file" == *.example ]] && fail "create $ROOT/configs/slot-$standby.env before flipping"
  require_public_cookie "$active_env_file" "$active"
  require_public_cookie "$standby_env_file" "$standby"
  backup_db="${BACKUP_DB:-}"
  [[ -n "$backup_db" && -f "$backup_db" ]] || \
    fail "BACKUP_DB must point to the final verified snapshot"
  keys_dir="${BACKUP_KEYS_DIR:-}"
  domain=$(edge_domain)
  log "active_slot=$active standby_slot=$standby target_slot=$standby"
  log "Precondition: operator must hold a write freeze and have go/no-go approval."
  set_slot_output_mode "$standby" rw
  docker compose -f "$SLOT_COMPOSE" --env-file "$standby_env_file" stop api || true
  restore_slot_db "$standby" "$backup_db" "$keys_dir"
  OUTPUT_MOUNT_MODE=rw docker compose -f "$SLOT_COMPOSE" --env-file "$standby_env_file" up -d --no-build api web
  d4_smoke_standby rw
  "$EDGE_SH" flip "$standby" --confirm-cutover || fail "edge flip failed"
  set_slot_output_mode "$active" ro
  smoke_public_url "$domain"
  log "FLIP complete: active_slot=$standby (was $active) target_output_mode=rw old_active_output_mode=ro."
}

d4_rollback() {
  require_confirm_cutover rollback
  local active target active_env_file target_env_file domain
  active=$(d4_active_slot)
  target=$(d4_standby_slot "$active")
  active_env_file=$(slot_env_file "$active")
  target_env_file=$(slot_env_file "$target")
  [[ "$active_env_file" == *.example ]] && fail "create $ROOT/configs/slot-$active.env before rollback"
  [[ "$target_env_file" == *.example ]] && fail "create $ROOT/configs/slot-$target.env before rollback"
  require_public_cookie "$active_env_file" "$active"
  require_public_cookie "$target_env_file" "$target"
  domain=$(edge_domain)
  log "active_slot=$active rollback_target=$target"
  set_slot_output_mode "$target" rw
  "$EDGE_SH" rollback --confirm-cutover || fail "edge rollback failed"
  set_slot_output_mode "$active" ro
  smoke_public_url "$domain"
  log "ROLLBACK complete: active_slot=$target (was $active) target_output_mode=rw old_active_output_mode=ro."
}

case "$MODE" in
  full)
    build_images
    verify_sandbox
    promote_prod
    log "DONE — sandbox then prod."
    ;;
  sandbox-only)
    build_images
    verify_sandbox
    log "STOPPED at sandbox for manual web/UI verification. Run '$0 --prod-only' when done."
    ;;
  prod-only)
    log "Deploying already-verified images (no build, no sandbox tests)."
    promote_prod
    log "DONE — prod-only."
    ;;
  status)
    d4_status
    ;;
  deploy-standby)
    d4_deploy_standby
    ;;
  smoke-standby)
    d4_smoke_standby
    ;;
  flip)
    d4_flip
    ;;
  rollback)
    d4_rollback
    ;;
esac
