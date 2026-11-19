#!/usr/bin/env bash
# Tests the membership/approve flow: grant Curator access on ONE library,
# confirm approve works there and is still blocked everywhere else.
# Assumes the API is already running and michael@example.com /
# wife@example.com already exist from test-api.sh (registers them if not).
set -euo pipefail

BASE="http://localhost:5000"
ADMIN_COOKIES=$(mktemp)
WIFE_COOKIES=$(mktemp)
trap 'rm -f "$ADMIN_COOKIES" "$WIFE_COOKIES"' EXIT

jget() {
  if command -v jq >/dev/null 2>&1; then jq -r "$1"
  else python3 -c "import sys,json; d=json.load(sys.stdin); print(d$2)"
  fi
}

section() { echo; echo "=== $1 ==="; }

section "0) Ensure both accounts exist (ignore 'already taken' if they do)"
curl -s -X POST "$BASE/api/auth/register" -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123","displayName":"Michael"}' > /dev/null || true
curl -s -X POST "$BASE/api/auth/register" -H "Content-Type: application/json" \
  -d '{"email":"wife@example.com","password":"Passw0rd!123","displayName":"Wife"}' > /dev/null || true

section "1) Log in as admin (michael)"
curl -s -c "$ADMIN_COOKIES" -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123"}' | tee /dev/stderr; echo

section "2) Find (or create) the 'Kids Movies' library"
LIBRARIES=$(curl -s -b "$ADMIN_COOKIES" "$BASE/api/virtual-libraries")
LIBRARY_ID=$(echo "$LIBRARIES" | jq -r '.[] | select(.name=="Kids Movies") | .id' 2>/dev/null || true)
if [ -z "${LIBRARY_ID:-}" ] || [ "$LIBRARY_ID" = "null" ]; then
  CREATE=$(curl -s -b "$ADMIN_COOKIES" -X POST "$BASE/api/virtual-libraries" -H "Content-Type: application/json" \
    -d '{"name":"Kids Movies","mediaType":"Movie","outputPath":"/tmp/curatarr-test/kids-movies"}')
  LIBRARY_ID=$(echo "$CREATE" | jget '.id' "['id']")
fi
echo "  -> Kids Movies library id: $LIBRARY_ID"

section "3) Find wife's user id"
USERS=$(curl -s -b "$ADMIN_COOKIES" "$BASE/api/users")
echo "$USERS"
WIFE_ID=$(echo "$USERS" | jq -r '.[] | select(.email=="wife@example.com") | .id' 2>/dev/null \
  || echo "$USERS" | python3 -c "import sys,json; print(next(u['id'] for u in json.load(sys.stdin) if u['email']=='wife@example.com'))")
echo "  -> wife's user id: $WIFE_ID"

section "3.5) Seed a real SourceRoot + SourceItem (approve needs a real FK target)"
ROOT=$(curl -s -b "$ADMIN_COOKIES" -X POST "$BASE/api/source-roots" -H "Content-Type: application/json" \
  -d '{"name":"Test Root","path":"/tmp/curatarr-test/source","mediaType":"Movie"}')
ROOT_ID=$(echo "$ROOT" | jget '.id' "['id']")
ITEM=$(curl -s -b "$ADMIN_COOKIES" -X POST "$BASE/api/source-items" -H "Content-Type: application/json" \
  -d "{\"sourceRootId\":\"$ROOT_ID\",\"relativePath\":\"Test Movie (2020)/Test Movie (2020).mkv\",\"mediaType\":\"Movie\",\"title\":\"Test Movie\"}")
ITEM_ID=$(echo "$ITEM" | jget '.id' "['id']")
echo "  -> seeded source item id: $ITEM_ID"

section "4) Grant wife Curator membership on Kids Movies"
curl -s -b "$ADMIN_COOKIES" -X POST "$BASE/api/virtual-libraries/$LIBRARY_ID/members" \
  -H "Content-Type: application/json" \
  -d "{\"userId\":\"$WIFE_ID\",\"role\":\"Curator\"}" | tee /dev/stderr; echo

section "5) Log in as wife"
curl -s -c "$WIFE_COOKIES" -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"wife@example.com","password":"Passw0rd!123"}' | tee /dev/stderr; echo

section "6) Wife approves an item into Kids Movies (expect 200 -- she's now a Curator there)"
curl -s -o /dev/null -w "HTTP status: %{http_code}\n" -b "$WIFE_COOKIES" \
  -X POST "$BASE/api/virtual-libraries/$LIBRARY_ID/approve" \
  -H "Content-Type: application/json" \
  -d "{\"sourceItemId\":\"$ITEM_ID\"}"

section "7) Wife tries to approve into a library she is NOT a member of (expect 403)"
FAKE_LIBRARY_ID="00000000-0000-0000-0000-000000000000"
curl -s -o /dev/null -w "HTTP status: %{http_code}\n" -b "$WIFE_COOKIES" \
  -X POST "$BASE/api/virtual-libraries/$FAKE_LIBRARY_ID/approve" \
  -H "Content-Type: application/json" \
  -d "{\"sourceItemId\":\"$ITEM_ID\"}"

echo
echo "Expect: step 6 = 200 (she has Curator membership on Kids Movies)"
echo "        step 7 = 403 (she has no membership on the other library)"
