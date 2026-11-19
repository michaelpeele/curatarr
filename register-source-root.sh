#!/usr/bin/env bash
# Registers a real source root with the running Curatarr.Api.
# Usage: ./register-source-root.sh "/Volumes/data/movies" "Real Movies"
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <path-to-media-folder> [root-name] [mediaType: Movie|Show]"
  echo "Example: $0 /Volumes/data/movies \"Real Movies\" Movie"
  exit 1
fi

MEDIA_PATH="$1"
ROOT_NAME="${2:-Real Movies}"
MEDIA_TYPE="${3:-Movie}"
BASE="http://localhost:5000"
COOKIES=$(mktemp)
trap 'rm -f "$COOKIES"' EXIT

echo "Logging in..."
curl -s -c "$COOKIES" -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123"}' > /dev/null

echo "Registering source root: $ROOT_NAME -> $MEDIA_PATH ($MEDIA_TYPE)"
curl -s -b "$COOKIES" -X POST "$BASE/api/source-roots" \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"$ROOT_NAME\",\"path\":\"$MEDIA_PATH\",\"mediaType\":\"$MEDIA_TYPE\"}"
echo
echo "Done. Now click \"Scan sources\" in the app to pick it up."
