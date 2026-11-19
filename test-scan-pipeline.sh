#!/usr/bin/env bash
# Full pipeline test through the API: real files on disk -> register root ->
# scan -> approve -> reconcile -> verify a real symlink was created.
set -euo pipefail

BASE="http://localhost:5000"
COOKIES=$(mktemp)
trap 'rm -f "$COOKIES"' EXIT

jget() {
  if command -v jq >/dev/null 2>&1; then jq -r "$1"
  else python3 -c "import sys,json; d=json.load(sys.stdin); print(d$2)"
  fi
}

section() { echo; echo "=== $1 ==="; }

TEST_ROOT="/tmp/curatarr-api-scan-test-$$"   # unique per run -- avoids colliding
SOURCE_DIR="$TEST_ROOT/media/movies"          # with SourceRoots left in the DB
VIRTUAL_DIR="$TEST_ROOT/media-curated/kids-movies"  # from previous runs
rm -rf "$TEST_ROOT"
mkdir -p "$SOURCE_DIR" "$VIRTUAL_DIR"

section "0) Create real fake movie files on disk"
mkdir -p "$SOURCE_DIR/Ratatouille (2007)"
echo "fake video bytes" > "$SOURCE_DIR/Ratatouille (2007)/Ratatouille (2007).mkv"
echo "  -> created $SOURCE_DIR/Ratatouille (2007)/Ratatouille (2007).mkv"

section "1) Log in as admin"
curl -s -c "$COOKIES" -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123"}' | tee /dev/stderr; echo

section "2) Register the source root via the API"
ROOT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/source-roots" -H "Content-Type: application/json" \
  -d "{\"name\":\"Test Movies\",\"path\":\"$SOURCE_DIR\",\"mediaType\":\"Movie\"}")
echo "$ROOT"

section "3) Trigger a scan"
curl -s -b "$COOKIES" -X POST "$BASE/api/scan" | tee /dev/stderr; echo

section "4) List source items -- should include Ratatouille"
ITEMS=$(curl -s -b "$COOKIES" "$BASE/api/source-items")
echo "$ITEMS"
RATATOUILLE_ID=$(echo "$ITEMS" | jq -r '.[] | select(.title=="Ratatouille") | .id' 2>/dev/null | head -n1)
if [ -z "$RATATOUILLE_ID" ]; then
  RATATOUILLE_ID=$(echo "$ITEMS" | python3 -c "import sys,json; print(next(i['id'] for i in json.load(sys.stdin) if i['title']=='Ratatouille'))")
fi
echo "  -> Ratatouille source item id: $RATATOUILLE_ID"

section "5) Create a virtual library pointed at a real output path"
LIBRARY=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries" -H "Content-Type: application/json" \
  -d "{\"name\":\"API Scan Test Library\",\"mediaType\":\"Movie\",\"outputPath\":\"$VIRTUAL_DIR\"}")
LIBRARY_ID=$(echo "$LIBRARY" | jget '.id' "['id']")
echo "  -> library id: $LIBRARY_ID"

section "6) Admin approves Ratatouille into that library"
curl -s -o /dev/null -w "HTTP status: %{http_code}\n" -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIBRARY_ID/approve" \
  -H "Content-Type: application/json" \
  -d "{\"sourceItemId\":\"$RATATOUILLE_ID\"}"

section "7) Reconcile"
curl -s -b "$COOKIES" -X POST "$BASE/api/reconcile" | tee /dev/stderr; echo

section "8) Verify the symlink actually exists on disk"
if [ -L "$VIRTUAL_DIR/Ratatouille (2007)/Ratatouille (2007).mkv" ]; then
  echo "  SUCCESS: symlink exists -> $(readlink "$VIRTUAL_DIR/Ratatouille (2007)/Ratatouille (2007).mkv")"
else
  echo "  FAILURE: expected symlink not found at $VIRTUAL_DIR/Ratatouille (2007)/Ratatouille (2007).mkv"
  find "$VIRTUAL_DIR" 2>/dev/null || echo "  (virtual dir doesn't even exist)"
fi
