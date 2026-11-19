#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EDGE_COMPOSE="${EDGE_COMPOSE:-$ROOT_DIR/docker-compose.edge.yml}"
ENV_FILE="${EDGE_ENV_FILE:-$ROOT_DIR/configs/edge.env}"

if [[ ! -f "$ENV_FILE" && -f "$ROOT_DIR/configs/edge.env.example" ]]; then
  ENV_FILE="$ROOT_DIR/configs/edge.env.example"
fi

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  . "$ENV_FILE"
  set +a
fi

TEMPLATE_FILE="${EDGE_TEMPLATE_FILE:-$ROOT_DIR/configs/Caddyfile.template}"
STATE_DIR="${EDGE_STATE_DIR:-$ROOT_DIR/state}"
ACTIVE_FILE="$STATE_DIR/active-slot"
RENDERED_FILE="$STATE_DIR/Caddyfile"

BLUE_WEB_PORT="${BLUE_WEB_PORT:-15173}"
GREEN_WEB_PORT="${GREEN_WEB_PORT:-15273}"
ACTIVE_SLOT="${ACTIVE_SLOT:-blue}"
CADDY_ADMIN="${CADDY_ADMIN:-localhost:2019}"
SMTP_FROM="${SMTP_FROM:-}"
CURATARR_DOMAIN="${CURATARR_DOMAIN:-}"
STANDBY_DOMAIN="${STANDBY_DOMAIN:-:5181}"

require_domain() {
  if [[ -z "$CURATARR_DOMAIN" || -z "$STANDBY_DOMAIN" ]]; then
    echo "CURATARR_DOMAIN and STANDBY_DOMAIN must be set" >&2
    exit 1
  fi
}

require_template() {
  if [[ ! -f "$TEMPLATE_FILE" ]]; then
    echo "Missing $TEMPLATE_FILE" >&2
    exit 1
  fi
}

require_compose_file() {
  if [[ ! -f "$EDGE_COMPOSE" ]]; then
    echo "Missing $EDGE_COMPOSE" >&2
    exit 1
  fi
}

read_active_slot() {
  local slot="${ACTIVE_SLOT}"
  if [[ -f "$ACTIVE_FILE" ]]; then
    slot="$(tr -d '[:space:]' < "$ACTIVE_FILE")"
  fi
  case "$slot" in
    blue|green) printf '%s' "$slot" ;;
    *) echo "Invalid active slot: $slot" >&2; exit 1 ;;
  esac
}

upstream_port() {
  local slot="$1"
  case "$slot" in
    blue) printf '%s' "$BLUE_WEB_PORT" ;;
    green) printf '%s' "$GREEN_WEB_PORT" ;;
    *) echo "Invalid slot: $slot" >&2; exit 1 ;;
  esac
}

standby_slot() {
  local slot="$1"
  case "$slot" in
    blue) printf 'green' ;;
    green) printf 'blue' ;;
    *) echo "Invalid slot: $slot" >&2; exit 1 ;;
  esac
}

email_line() {
  if [[ -n "$SMTP_FROM" ]]; then
    printf 'email %s' "$SMTP_FROM"
  fi
}

render() {
  require_domain
  require_template
  local slot standby upstream standby_upstream email
  slot="$(read_active_slot)"
  standby="$(standby_slot "$slot")"
  upstream="$(upstream_port "$slot")"
  standby_upstream="$(upstream_port "$standby")"
  email="$(email_line)"
  mkdir -p "$STATE_DIR"
  sed -e "s|__UPSTREAM_PORT__|${upstream}|g" \
      -e "s|__STANDBY_UPSTREAM_PORT__|${standby_upstream}|g" \
      -e "s|__CADDY_EMAIL_LINE__|${email}|g" \
      "$TEMPLATE_FILE" > "$RENDERED_FILE"
  printf 'Rendered Caddyfile for active slot %s (upstream 127.0.0.1:%s), standby slot %s (upstream 127.0.0.1:%s) to %s\n' \
    "$slot" "$upstream" "$standby" "$standby_upstream" "$RENDERED_FILE"
}

status() {
  local slot standby upstream standby_upstream
  slot="$(read_active_slot)"
  standby="$(standby_slot "$slot")"
  upstream="$(upstream_port "$slot")"
  standby_upstream="$(upstream_port "$standby")"
  printf 'active_slot=%s\n' "$slot"
  printf 'upstream=127.0.0.1:%s\n' "$upstream"
  printf 'standby_slot=%s\n' "$standby"
  printf 'standby_upstream=127.0.0.1:%s\n' "$standby_upstream"
  printf 'env_file=%s\n' "$ENV_FILE"
}

write_active_slot() {
  local slot="$1"
  case "$slot" in
    blue|green) ;;
    *) echo "Invalid slot: $slot" >&2; exit 1 ;;
  esac
  mkdir -p "$STATE_DIR"
  printf '%s' "$slot" > "$ACTIVE_FILE"
}

reload() {
  render
  require_compose_file
  docker compose -f "$EDGE_COMPOSE" exec -T edge \
    caddy reload --config /workspace/state/Caddyfile --adapter caddyfile
}

require_confirm() {
  if [[ "${2:-}" != "--confirm-cutover" ]]; then
    echo "Refusing to change active slot without --confirm-cutover" >&2
    exit 1
  fi
}

flip() {
  local target="${1:-}"
  require_confirm "$@"
  case "$target" in
    blue|green) ;;
    *) echo "Usage: scripts/edge.sh flip <blue|green> --confirm-cutover" >&2; exit 1 ;;
  esac
  local current
  current="$(read_active_slot)"
  if [[ "$current" == "$target" ]]; then
    printf 'Active slot is already %s; no change.\n' "$target"
    return 0
  fi
  write_active_slot "$target"
  render
  reload
  printf 'Active slot changed to %s.\n' "$target"
}

rollback() {
  local current target
  require_confirm rollback "$@"
  current="$(read_active_slot)"
  if [[ "$current" == "blue" ]]; then target="green"; else target="blue"; fi
  write_active_slot "$target"
  render
  reload
  printf 'Rolled back to slot %s.\n' "$target"
}

entrypoint() {
  render
  exec caddy run --config "$RENDERED_FILE" --adapter caddyfile
}

usage() {
  cat <<'EOF'
Usage: scripts/edge.sh <command> [options]

Commands:
  status                         Print active/standby slots and upstream ports.
  render                         Render state/Caddyfile without changing routing.
  flip <blue|green> --confirm-cutover
                                 Change active slot and reload Caddy.
  rollback --confirm-cutover      Switch to the opposite slot and reload Caddy.
  reload                         Re-render and reload Caddy.
  entrypoint                     Render for the edge container and run Caddy.

Production cutover requires an explicit maintenance window, final snapshot,
go/no-go approval, and the --confirm-cutover flag.

Scratch overrides: EDGE_ENV_FILE, EDGE_TEMPLATE_FILE, EDGE_STATE_DIR,
EDGE_COMPOSE. Defaults stay on production config/state paths.
EOF
}

case "${1:-}" in
  status) status ;;
  render) render ;;
  flip) shift; flip "$@" ;;
  rollback) shift; rollback "$@" ;;
  reload) reload ;;
  entrypoint) entrypoint ;;
  *) usage; exit 1 ;;
esac
