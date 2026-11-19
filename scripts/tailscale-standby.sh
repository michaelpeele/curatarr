#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="${TS_CONTAINER:-ts-curatarr}"
SERVICE="svc:curatarr-standby"
ENDPOINT="http://localhost:5181"
STATE_DIR="$ROOT_DIR/state/curatarr-tailscale-data"
CONFIG_FILE="$STATE_DIR/standby-serve.json"
CONTAINER_CONFIG="/var/lib/tailscale/standby-serve.json"
PRIMARY_TARGET="http://172.22.0.1:5180"
PRIMARY_HOST="https://curatarr.tail0307d.ts.net"
PERSISTED_SERVE="$ROOT_DIR/curatarr-tailscale-data/serve.json"

require_container() {
  local running
  running="$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null || true)"
  [[ "$running" == "true" ]] || {
    echo "Container $CONTAINER is not running" >&2
    exit 1
  }
}

render() {
  mkdir -p "$STATE_DIR"
  # set-config --service takes the service details; SERVICE is passed as a flag.
  cat > "$CONFIG_FILE" <<EOF
{
  "version": "0.0.1",
  "endpoints": {
    "tcp:443": "$ENDPOINT"
  }
}
EOF
  chmod 600 "$CONFIG_FILE"
  printf 'Rendered standby service config: %s\n' "$CONFIG_FILE"
}

verify_primary() {
  local funnel
  funnel="$(docker exec "$CONTAINER" tailscale funnel status)"
  printf '%s\n' "$funnel" | grep -F "$PRIMARY_HOST (Funnel on)" >/dev/null
  printf '%s\n' "$funnel" | grep -F "$PRIMARY_TARGET" >/dev/null
  grep -F "$PRIMARY_TARGET" "$PERSISTED_SERVE" >/dev/null
  grep -F '"${TS_CERT_DOMAIN}:443": true' "$PERSISTED_SERVE" >/dev/null
  printf 'Primary Funnel unchanged: %s -> %s\n' "$PRIMARY_HOST" "$PRIMARY_TARGET"
}

apply() {
  require_container
  render
  verify_primary
  docker exec "$CONTAINER" sh -c \
    'wget -q -O /dev/null http://172.22.0.1:5181/api/bootstrapped'
  docker cp "$CONFIG_FILE" "$CONTAINER:$CONTAINER_CONFIG" >/dev/null
  docker exec "$CONTAINER" tailscale serve set-config \
    --service="$SERVICE" "$CONTAINER_CONFIG"
  printf 'Applied Tailscale Service %s -> %s\n' "$SERVICE" "$ENDPOINT"
}

status() {
  require_container
  printf 'Primary Funnel:\n'
  docker exec "$CONTAINER" tailscale funnel status
  printf '\nTailscale Services:\n'
  docker exec "$CONTAINER" tailscale serve get-config --all
}

verify_standby() {
  require_container
  docker exec "$CONTAINER" tailscale serve get-config --all | grep -F "$SERVICE" >/dev/null
  docker exec "$CONTAINER" sh -c "wget -q -O /dev/null $ENDPOINT/api/bootstrapped"
  curl -fsS -o /dev/null http://127.0.0.1:5181/api/bootstrapped
  printf 'Standby service ready: %s\n' "$ENDPOINT"
}

usage() {
  cat <<'EOF'
Usage: scripts/tailscale-standby.sh <command>

Commands:
  render   Write the ignored huJSON config under state/curatarr-tailscale-data.
  apply    Render, verify the primary Funnel, and apply svc:curatarr-standby.
  status   Print the primary Funnel and Tailscale Service configuration.
  verify   Verify the primary Funnel and tailnet standby endpoint.
EOF
}

case "${1:-apply}" in
  render) render ;;
  apply) apply ;;
  status) status ;;
  verify) verify_primary; verify_standby ;;
  *) usage; exit 1 ;;
esac
