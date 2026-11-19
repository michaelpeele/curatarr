#!/usr/bin/env bash
# Exercises the Curatarr.Api auth/roles/membership pipeline end to end.
# Run this AFTER starting the API in another terminal:
#   cd Curatarr.Api && dotnet run
# then, in a second terminal:
#   ./test-api.sh
#
# Against the sandbox (see docker-compose.sandbox.yml):
#   BASE=http://localhost:5001 ./test-api.sh                  # full suite
#   BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh    # wizard tests only
#     (fresh/empty DB -> bootstrap happy path + one-shot guard;
#      bootstrapped DB -> one-shot guard only, zero writes)
#
# WARNING: the full suite CREATES users and libraries — only run it
# against a fresh instance or the sandbox, never against prod.
set -euo pipefail

BASE="${BASE:-http://localhost:5000}"   # override via BASE=... (e.g. sandbox port 5001)
COOKIES=$(mktemp)
EXTRA_TEMPS=""
trap 'rm -f "$COOKIES" $EXTRA_TEMPS' EXIT

section() { echo; echo "=== $1 ==="; }

json_field() { # json_field <json> <key> -> value via jq or python3 fallback
  if command -v jq >/dev/null 2>&1; then
    echo "$1" | jq -r ".${2}"
  else
    echo "$1" | python3 -c "import sys, json; print(str(json.load(sys.stdin)['${2}']).lower())"
  fi
}

# One-shot guard: POST /api/bootstrap must be rejected (400 + message)
# once at least one user exists. Anonymous, zero writes on rejection.
assert_bootstrap_guard() {
  local out code
  out=$(curl -s -w "\n%{http_code}" -X POST "$BASE/api/bootstrap" \
    -H "Content-Type: application/json" \
    -d '{"adminUsername":"guard-test@example.com","adminPassword":"Passw0rd!123","adminDisplayName":"Guard","sourceRoots":[],"pathMappings":[],"libraryName":"Guard","libraryOutputPath":"/tmp/guard","mediaType":"Movie"}')
  code=$(echo "$out" | tail -1)
  echo "$out"
  if [ "$code" = "400" ] && echo "$out" | grep -q "already exists"; then
    echo "  -> PASS: one-shot guard holds (400, 'already exists')"
  else
    echo "  -> FAIL: expected 400 'already exists', got HTTP $code"
    exit 1
  fi
}

# ── Section 0: wizard gate (isBootstrapped + bootstrap coverage) ─────────
section "0) Wizard gate: isBootstrapped + one-shot bootstrap guard"
BOOT_JSON=$(curl -s "$BASE/api/bootstrapped")
echo "$BOOT_JSON"
IS_BOOTSTRAPPED=$(json_field "$BOOT_JSON" isBootstrapped)
case "$IS_BOOTSTRAPPED" in
  true|false) echo "  -> isBootstrapped=$IS_BOOTSTRAPPED" ;;
  *) echo "  -> FAIL: unexpected /api/bootstrapped payload"; exit 1 ;;
esac

if [ "${WIZARD_ONLY:-0}" = "1" ]; then
  if [ "$IS_BOOTSTRAPPED" = "false" ]; then
    echo "  -> fresh DB: running bootstrap happy path"
    RESULT=$(curl -s -X POST "$BASE/api/bootstrap" \
      -H "Content-Type: application/json" \
      -d '{"adminUsername":"michael@example.com","adminPassword":"Passw0rd!123","adminDisplayName":"Michael","sourceRoots":[{"name":"Test Movies","path":"/tmp/curatarr-test/media"}],"pathMappings":[{"fromPrefix":"/media","toPrefix":"/mnt/data"}],"libraryName":"Wizard Library","libraryOutputPath":"/tmp/curatarr-test/wizard","mediaType":"Movie"}')
    echo "$RESULT"
    echo "$RESULT" | grep -q "Bootstrap complete" || { echo "  -> FAIL: bootstrap did not complete"; exit 1; }

    AFTER=$(curl -s "$BASE/api/bootstrapped")
    echo "$AFTER" | grep -q '"isBootstrapped":true' || { echo "  -> FAIL: still not bootstrapped after bootstrap"; exit 1; }

    # wizard-created admin must be able to log in with the Admin role
    curl -s -c "$COOKIES" -b "$COOKIES" -X POST "$BASE/api/auth/login" \
      -H "Content-Type: application/json" \
      -d '{"email":"michael@example.com","password":"Passw0rd!123"}' > /dev/null
    ME=$(curl -s -b "$COOKIES" "$BASE/api/me")
    echo "$ME"
    echo "$ME" | grep -q '"Admin"' || { echo "  -> FAIL: wizard admin lacks the Admin role"; exit 1; }
    echo "  -> PASS: happy path (bootstrap -> isBootstrapped=true -> login -> Admin role)"
  else
    echo "  -> bootstrapped DB: skipping happy path (guard-only mode)"
  fi
  assert_bootstrap_guard
  echo
  echo "WIZARD_ONLY=1 set -- stopping after section 0."
  exit 0
fi
# Full-suite mode: state is asserted, guard runs at the end (step 10)
# once a user definitely exists. On a fresh DB expect false here and
# true after step 1's register.
if [ "$IS_BOOTSTRAPPED" = "false" ]; then
  echo "  -> fresh DB: expecting isBootstrapped=true after step 1 (register)"
fi

section "1) Register the first user (becomes Admin automatically)"
curl -s -c "$COOKIES" -X POST "$BASE/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123","displayName":"Michael"}' | tee /dev/stderr
echo

section "2) Log in as that user (cookie stored in $COOKIES)"
curl -s -c "$COOKIES" -b "$COOKIES" -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com","password":"Passw0rd!123"}' | tee /dev/stderr
echo

section "3) Check /api/me -- should show Admin role"
curl -s -b "$COOKIES" "$BASE/api/me" | tee /dev/stderr
echo

section "4) Create a virtual library (Admin-only endpoint)"
LIBRARY_JSON=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries" \
  -H "Content-Type: application/json" \
  -d '{"name":"Kids Movies","mediaType":"Movie","outputPath":"/tmp/curatarr-test/kids-movies"}')
echo "$LIBRARY_JSON"
if command -v jq >/dev/null 2>&1; then
  LIBRARY_ID=$(echo "$LIBRARY_JSON" | jq -r '.id')
else
  LIBRARY_ID=$(echo "$LIBRARY_JSON" | python3 -c "
import sys, json
print(json.load(sys.stdin)['id'])
")
fi
echo "  -> captured library id: $LIBRARY_ID"

section "5) Register a second user (should default to Viewer, not Admin)"
curl -s -X POST "$BASE/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"email":"wife@example.com","password":"Passw0rd!123","displayName":"Wife"}' | tee /dev/stderr
echo

section "6) Sanity check: non-admin cannot create a library (expect 403)"
WIFE_COOKIES=$(mktemp)
curl -s -c "$WIFE_COOKIES" -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"wife@example.com","password":"Passw0rd!123"}' > /dev/null
curl -s -o /dev/null -w "HTTP status: %{http_code}\n" -b "$WIFE_COOKIES" -X POST "$BASE/api/virtual-libraries" \
  -H "Content-Type: application/json" \
  -d '{"name":"Should Fail","mediaType":"Movie","outputPath":"/tmp/nope"}'
rm -f "$WIFE_COOKIES"

echo
echo "If step 6 printed 'HTTP status: 403', the Admin-only role gate is working correctly."

section "7) Invite a new user (expect 200 + message)"
curl -s -b "$COOKIES" -X POST "$BASE/api/users/invite" \
  -H "Content-Type: application/json" \
  -d '{"email":"invited@example.com"}' | tee /dev/stderr
echo

section "8) Re-invite the SAME pending user (regression: was 400, expect 200 're-sent')"
REINVITE_BODY=$(curl -s -b "$COOKIES" -X POST "$BASE/api/users/invite" \
  -H "Content-Type: application/json" \
  -d '{"email":"invited@example.com"}')
echo "$REINVITE_BODY" | tee /dev/stderr
echo
if echo "$REINVITE_BODY" | grep -qi "re-sent"; then
  echo "  -> PASS: pending invite was resent, not rejected"
else
  echo "  -> FAIL: expected a 're-sent' message"
  exit 1
fi

section "9) Invite an already-active account (expect friendly 400)"
ACTIVE_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -b "$COOKIES" -X POST "$BASE/api/users/invite" \
  -H "Content-Type: application/json" \
  -d '{"email":"michael@example.com"}')
echo "HTTP status: $ACTIVE_STATUS (expected 400)"
[ "$ACTIVE_STATUS" = "400" ] || { echo "  -> FAIL"; exit 1; }
echo "  -> PASS"

# Opt-in: the sandbox exposes an Admin-only token sink (Testing__InviteSink=1)
# so this can exercise the full accept-invite flow without scraping docker logs.
if [ "${INVITE_E2E:-0}" = "1" ]; then
  section "9a) Accept invite end-to-end (sandbox token sink, opt-in)"
  INVITE_SNAPSHOT=$(curl -s -b "$COOKIES" "$BASE/api/test/invites")
  INVITE_TOKEN=$(python3 -c 'import json,sys; rows=json.load(sys.stdin); row=[r for r in rows if r.get("email")=="invited@example.com"][-1]; print(row["token"])' <<< "$INVITE_SNAPSHOT")
  INVITE_USER_ID=$(python3 -c 'import json,sys; rows=json.load(sys.stdin); row=[r for r in rows if r.get("email")=="invited@example.com"][-1]; print(row["userId"])' <<< "$INVITE_SNAPSHOT")
  if [ -z "$INVITE_TOKEN" ] || [ -z "$INVITE_USER_ID" ]; then
    echo "  -> FAIL: no captured invite for invited@example.com (is Testing__InviteSink enabled?)"
    exit 1
  fi
  ACCEPT_BODY=$(python3 -c 'import json,sys; print(json.dumps({"userId":sys.argv[1],"token":sys.argv[2],"password":"Passw0rd!123","displayName":"Invited User"}))' "$INVITE_USER_ID" "$INVITE_TOKEN")
  ACCEPT_OUT=$(curl -s -w "\n%{http_code}" -X POST "$BASE/api/auth/accept-invite" \
    -H "Content-Type: application/json" -d "$ACCEPT_BODY")
  ACCEPT_CODE=$(echo "$ACCEPT_OUT" | tail -1)
  ACCEPT_JSON=$(echo "$ACCEPT_OUT" | head -n -1)
  echo "$ACCEPT_JSON" | tee /dev/stderr
  [ "$ACCEPT_CODE" = "200" ] || { echo "  -> FAIL: accept-invite returned HTTP $ACCEPT_CODE"; exit 1; }
  ACCEPT_EMAIL=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["email"])' <<< "$ACCEPT_JSON")

  INVITE_COOKIES=$(mktemp)
  EXTRA_TEMPS="$EXTRA_TEMPS $INVITE_COOKIES"
  curl -s -c "$INVITE_COOKIES" -X POST "$BASE/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$ACCEPT_EMAIL\",\"password\":\"Passw0rd!123\"}" > /dev/null
  ME_BODY=$(curl -s -b "$INVITE_COOKIES" "$BASE/api/me")
  echo "$ME_BODY" | tee /dev/stderr
  echo "$ME_BODY" | grep -q '"Viewer"' || { echo "  -> FAIL: accepted user is not Viewer"; exit 1; }

  REPLAY_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/accept-invite" \
    -H "Content-Type: application/json" -d "$ACCEPT_BODY")
  [ "$REPLAY_STATUS" = "400" ] || { echo "  -> FAIL: one-time token replay returned HTTP $REPLAY_STATUS"; exit 1; }
  DELETE_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -b "$COOKIES" -X DELETE "$BASE/api/users/$INVITE_USER_ID")
  [ "$DELETE_STATUS" = "200" ] || { echo "  -> FAIL: cleanup delete returned HTTP $DELETE_STATUS"; exit 1; }
  echo "  -> PASS: accepted, logged in as Viewer, replay rejected, test user cleaned up"
else
  echo
  echo "Invite accept E2E skipped (set INVITE_E2E=1 with Testing__InviteSink enabled to run)."
fi

# Opt-in: global role endpoint + guards. Creates no users; temporarily
# reshuffles the roles of the two users this suite already created, then
# restores them. Sandbox only (the full suite itself is sandbox-only).
if [ "${ROLE_E2E:-0}" = "1" ]; then
  section "9b) Global roles endpoint + guards (opt-in, sandbox only)"
  ROLE_WIFE_COOKIES=$(mktemp)
  ROLE_ADMIN2_COOKIES=$(mktemp)
  ROLE_RESTORE_COOKIES=$(mktemp)
  EXTRA_TEMPS="$EXTRA_TEMPS $ROLE_WIFE_COOKIES $ROLE_ADMIN2_COOKIES $ROLE_RESTORE_COOKIES"
  (
    set -euo pipefail
    curl -s -c "$ROLE_WIFE_COOKIES" -X POST "$BASE/api/auth/login" \
      -H "Content-Type: application/json" \
      -d '{"email":"wife@example.com","password":"Passw0rd!123"}' > /dev/null

    USERS_JSON=$(curl -s -b "$COOKIES" "$BASE/api/users")
    echo "$USERS_JSON" | tee /dev/stderr
    ADMIN_ID=$(python3 -c 'import json,sys; print([u for u in json.load(sys.stdin) if u["email"]=="michael@example.com"][0]["id"])' <<< "$USERS_JSON")
    WIFE_ID=$(python3 -c 'import json,sys; print([u for u in json.load(sys.stdin) if u["email"]=="wife@example.com"][0]["id"])' <<< "$USERS_JSON")
    [ -n "$ADMIN_ID" ] && [ -n "$WIFE_ID" ] || { echo "  -> FAIL: could not resolve user ids"; exit 1; }
    echo "$USERS_JSON" | python3 -c 'import json,sys; assert any("Admin" in u["roles"] for u in json.load(sys.stdin))' || { echo "  -> FAIL: roles missing from /api/users"; exit 1; }

    # Restore from whichever state the test reached. The promoted admin's cookie
    # promotes the original admin back; a fresh original-admin cookie then
    # demotes the promoted admin (neither can self-demote).
    restore_roles() {
      if [ -s "$ROLE_ADMIN2_COOKIES" ]; then
        curl -s -o /dev/null -b "$ROLE_ADMIN2_COOKIES" -X PUT "$BASE/api/users/$ADMIN_ID/roles" \
          -H "Content-Type: application/json" -d '{"roles":["Admin","Viewer"]}' || true
      fi
      curl -s -c "$ROLE_RESTORE_COOKIES" -X POST "$BASE/api/auth/login" \
        -H "Content-Type: application/json" \
        -d '{"email":"michael@example.com","password":"Passw0rd!123"}' > /dev/null || true
      curl -s -o /dev/null -b "$ROLE_RESTORE_COOKIES" -X PUT "$BASE/api/users/$WIFE_ID/roles" \
        -H "Content-Type: application/json" -d '{"roles":["Viewer"]}' || true
    }
    trap restore_roles EXIT

    put_roles() { # put_roles <cookie jar> <user id> <roles json> -> http status
      curl -s -o /dev/null -w "%{http_code}" -b "$1" -X PUT "$BASE/api/users/$2/roles" \
        -H "Content-Type: application/json" -d "$3"
    }

    STATUS=$(put_roles "$ROLE_WIFE_COOKIES" "$WIFE_ID" '{"roles":["Admin"]}')
    [ "$STATUS" = "403" ] || { echo "  -> FAIL: Viewer role PUT returned $STATUS (expected 403)"; exit 1; }

    # The original admin cannot drop their own Admin role.
    STATUS=$(put_roles "$COOKIES" "$ADMIN_ID" '{"roles":["Viewer"]}')
    [ "$STATUS" = "400" ] || { echo "  -> FAIL: self-demotion returned $STATUS (expected 400)"; exit 1; }

    # Promote the Viewer, then demote the original admin (two admins exist,
    # so that is legal). The wife is now the only Admin.
    STATUS=$(put_roles "$COOKIES" "$WIFE_ID" '{"roles":["Admin","Viewer"]}')
    [ "$STATUS" = "200" ] || { echo "  -> FAIL: promotion returned $STATUS"; exit 1; }
    curl -s -c "$ROLE_ADMIN2_COOKIES" -X POST "$BASE/api/auth/login" \
      -H "Content-Type: application/json" \
      -d '{"email":"wife@example.com","password":"Passw0rd!123"}' > /dev/null

    # Demote the original admin with the NEW admin's cookie: a self-demotion
    # guard would reject the original admin's own cookie here.
    STATUS=$(put_roles "$ROLE_ADMIN2_COOKIES" "$ADMIN_ID" '{"roles":["Viewer"]}')
    [ "$STATUS" = "200" ] || { echo "  -> FAIL: admin demotion returned $STATUS"; exit 1; }

    # Last-admin guard: the sole remaining Admin cannot be demoted. The old
    # original-admin cookie still carries its Admin claim, but its user is no
    # longer the target, so this exercises the last-admin check rather than
    # the self-demotion check.
    STATUS=$(put_roles "$COOKIES" "$WIFE_ID" '{"roles":["Viewer"]}')
    [ "$STATUS" = "400" ] || { echo "  -> FAIL: last-admin demotion returned $STATUS (expected 400)"; exit 1; }

    restore_roles
    trap - EXIT
    FINAL=$(curl -s -b "$COOKIES" "$BASE/api/users")
    echo "$FINAL" | python3 -c 'import json,sys; us={u["email"]:u["roles"] for u in json.load(sys.stdin)}; assert "Admin" in us["michael@example.com"], us; assert "Admin" not in us["wife@example.com"], us' || { echo "  -> FAIL: roles were not restored"; exit 1; }
    echo "  -> PASS: Admin-only PUT, self-demotion 400, last-admin 400, promotion/demotion + cleanup"
  )
else
  echo
  echo "Roles endpoint E2E skipped (set ROLE_E2E=1 to run against the sandbox)."
fi

section "10) Wizard one-shot guard (users now exist, expect 400 'already exists')"
GUARD_IS_BOOTSTRAPPED=$(json_field "$(curl -s "$BASE/api/bootstrapped")" isBootstrapped)
[ "$GUARD_IS_BOOTSTRAPPED" = "true" ] || { echo "  -> FAIL: isBootstrapped should be true after register"; exit 1; }
echo "  -> isBootstrapped=true (state transition verified)"
assert_bootstrap_guard

echo
echo "ALL SECTIONS PASSED."
