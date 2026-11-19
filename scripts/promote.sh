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

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

MODE=full
SEED=0
for arg in "$@"; do
  case "$arg" in
    --sandbox-only|--prod-only) MODE="${arg#--}" ;;
    --seed) SEED=1 ;;
    -h|--help)
      sed -n '2,12p' "${BASH_SOURCE[0]}"
      exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

SANDBOX_COMPOSE=docker-compose.sandbox.yml
LOG_FILE="$ROOT/logs/promotions.log"
mkdir -p "$(dirname "$LOG_FILE")"

log() { echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] [$MODE] $*" | tee -a "$LOG_FILE"; }
on_error() {
  local rc=$?
  trap - ERR
  log "FAILED (exit $rc) at line $1 — prod was not promoted from this run."
  exit "$rc"
}
trap 'on_error $LINENO' ERR

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
esac
