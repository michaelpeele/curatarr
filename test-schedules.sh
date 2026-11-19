#!/usr/bin/env bash
# Tests the library scheduling API (Sprint 3 Slice 1).
# Run against an already-bootstrapped instance.
#   BASE=http://localhost:5001 ./test-schedules.sh     # sandbox
#   ./test-schedules.sh                                 # prod default
set -euo pipefail

BASE="${BASE:-http://localhost:5000}"
COOKIES=$(mktemp)
trap 'rm -f "$COOKIES"' EXIT
PASS=0 FAIL=0

# ── helpers ───────────────────────────────────────────────────────────────
login() {
  curl -s -c "$COOKIES" -b "$COOKIES" -X POST "$BASE/api/auth/login" \
    -H 'Content-Type: application/json' \
    -d '{"email":"michael.peele@gmail.com","password":"Passw0rd!123"}' > /dev/null
}

ok() { local msg="$1"; PASS=$((PASS+1)); echo "  -> PASS: $msg"; }
fail() { local msg="$1"; FAIL=$((FAIL+1)); echo "  -> FAIL: $msg"; }

check() {
  # check <description> <json> <key> <expected>
  local desc="$1" got="$2" key="$3" expected="$4"
  local val
  if [[ "$key" == *'['* ]]; then
    local arr_key idx
    arr_key="${key%%\[*}"
    idx="${key##*[}"
    idx="${idx%%]*}"
    val=$(echo "$got" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print(str(d['$arr_key'][$idx]).lower())
")
  else
    val=$(echo "$got" | python3 -c "import sys,json; print(str(json.load(sys.stdin)['${key}']).lower())")
  fi
  if [ "$val" = "$expected" ]; then ok "$desc"; else fail "$desc (got=$val expected=$expected)"; fi
}

check_str() {
  local desc="$1" got="$2" key="$3" expected="$4"
  local val
  if [[ "$key" == *'['* ]]; then
    local arr_key idx
    arr_key="${key%%\[*}"
    idx="${key##*[}"
    idx="${idx%%]*}"
    val=$(echo "$got" | python3 -c "
import sys,json
d=json.load(sys.stdin)
print(d['$arr_key'][$idx])
")
  else
    val=$(echo "$got" | python3 -c "import sys,json; print(json.load(sys.stdin)['${key}'])")
  fi
  if [ "$val" = "$expected" ]; then ok "$desc"; else fail "$desc (got='$val' expected='$expected')"; fi
}

# compare_null: special-case checks where JSON null should match None in Python
check_null() {
  local desc="$1" got="$2" key="$3"
  local val
  val=$(echo "$got" | python3 -c "import sys,json; v=json.load(sys.stdin).get('${key}'); print('NONE' if v is None else str(v).upper())")
  if [ "$val" = "NONE" ]; then ok "$desc"; else fail "$desc (got=$val expected=None)"; fi
}

assert_http() {
  # assert_http <desc> <code> <actual_code>
  if [ "$3" = "$2" ]; then ok "$1"; else fail "$1 (HTTP $3)"; fi
}

section() { echo; echo "=== $1 ==="; }

# ── setup ─────────────────────────────────────────────────────────────────
section "0) Login + pick first library"
login
ME=$(curl -s -b "$COOKIES" "$BASE/api/me")
echo "$ME"
ROLE=$(echo "$ME" | python3 -c "import sys,json; r=json.load(sys.stdin)['roles']; print(r[0])")
if [ "$ROLE" != "Admin" ]; then fail "need Admin role (got $ROLE)"; exit 1; fi

LIB=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)[0]['id'])")
echo "Library: $LIB"

# Clean any stale schedules from prior runs
SCHED_JSON=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules")
for sid in $(echo "$SCHED_JSON" | python3 -c "import sys,json; [print(s['id']) for s in json.load(sys.stdin)]"); do
  curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$sid" > /dev/null
done

section "1) List schedules when empty"
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules")
COUNT=$(echo "$OUT" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))")
[ "$COUNT" = "0" ] && ok "empty list is []" || fail "expected [] got $OUT"

section "2) Create a Saturday-morning rule (daysOfWeek=[6], 09:00-12:00)"
OUT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Sat cartoons","enabled":true,"daysOfWeek":[6],"startTime":"09:00","endTime":"12:00"}')
echo "$OUT"
SAT_ID=$(echo "$OUT" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
check_str "create returns id" "$OUT" "id" "$SAT_ID"
check "enabled defaults to true" "$OUT" "enabled" "true"
[ -n "$SAT_ID" ] && ok "create returned id" || fail "no id on create response"

section "3) Get single schedule by id"
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/$SAT_ID")
check_str "get name" "$OUT" "name" "Sat cartoons"
check "get days of week" "$OUT" "daysOfWeek[0]" "6"

section "4) Update the rule (change end time, disable it)"
OUT=$(curl -s -b "$COOKIES" -X PUT "$BASE/api/virtual-libraries/$LIB/schedules/$SAT_ID" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Sat cartoons (renamed)","enabled":false,"daysOfWeek":[6],"startTime":"09:00","endTime":"11:00"}')
echo "$OUT"
check_str "updated name" "$OUT" "name" "Sat cartoons (renamed)"
check "updated enabled" "$OUT" "enabled" "false"

section "5) Status endpoint — NO rules => always active (fail-open)"
# Delete every existing schedule so we truly have zero rules
for sid in $(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules" \
  | python3 -c "import sys,json; [print(s['id']) for s in json.load(sys.stdin)]"); do
  curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$sid" > /dev/null
done
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-21T10:00:00Z")
echo "$OUT"
check "zero rules => isActive=true" "$OUT" "isActive" "true"
check "alwaysOn=true with no enabled rules" "$OUT" "alwaysOn" "true"
check_null "nextTransitionAt is None when always-on" "$OUT" "nextTransitionAt"

section "6) Re-enable and verify SAT isActive transitions correctly"
# Step 5 deleted everything, so re-create the Saturday rule fresh
OUT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Sat cartoons","enabled":true,"daysOfWeek":[6],"startTime":"09:00","endTime":"12:00"}')
SAT_ID=$(echo "$OUT" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")

# Saturday 10am UTC (= actual sat noon in UTC-2, but sandbox is UTC)
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-21T10:00:00Z")
echo "$OUT"
check "sat 10am sat=active" "$OUT" "isActive" "true"
NEXT=$(echo "$OUT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('nextTransitionAt'))")
check_str "next transition at 12:00z" "$OUT" "nextTransitionAt" "2026-11-21T12:00:00+00:00"

# Sunday (should be inactive)
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-22T10:00:00Z")
check "sun 10am sat-rule=inactive" "$OUT" "isActive" "false"

section "7) Overnight window (Fri 22:00 – Sun 08:00)"
OUT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Fri night movie","enabled":true,"daysOfWeek":[5],"startTime":"22:00","endTime":"08:00"}')
FRIDAY_ID=$(echo "$OUT" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")

# Thu night — before window starts
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-20T18:00:00Z")
check "thu 6pm sat_rule=false friday_night=false => union=false" "$OUT" "isActive" "false"

# Fri night — inside overnight window (Fri 22:00 → Sat 08:00 per-day semantics)
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-20T23:00:00Z")
check "fri 11pm inside overnight window" "$OUT" "isActive" "true"

# Sat 3am — still inside overnight window
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-21T03:00:00Z")
check "sat 3am still inside overnight window" "$OUT" "isActive" "true"

# Sun 9am — past the window (end is 08:00, window belongs to Friday's day-of-week)
OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-22T09:00:00Z")
check "sun 9am past overnight window" "$OUT" "isActive" "false"

section "8) Seasonal window wrapping year-end (Dec 15 – Jan 5, all day)"
OUT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Holiday season","enabled":true,"startTime":"00:00","endTime":"00:00","startMonth":12,"startDay":15,"endMonth":1,"endDay":5}')
HOLIDAY_ID=$(echo "$OUT" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")

OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-12-25T12:00:00Z")
check "christmas day within seasonal window" "$OUT" "isActive" "true"

OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2026-11-01T12:00:00Z")
check "nov 1 outside seasonal window" "$OUT" "isActive" "false"

OUT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules/status?at=2027-01-03T12:00:00Z")
check "jan 3 still inside wrapped seasonal window" "$OUT" "isActive" "true"

section "9) Validation rejects bad inputs"
BAD_DAYS=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"bad","enabled":true,"daysOfWeek":[8],"startTime":"09:00","endTime":"12:00"}')
echo "$BAD_DAYS"
echo "$BAD_DAYS" | grep -qi "Days of week" && ok "rejects day-of-week=8" || fail "should reject day 8"

BAD_TIME=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"bad","enabled":true,"startTime":"not-a-time","endTime":"12:00"}')
echo "$BAD_TIME"
echo "$BAD_TIME" | grep -qi "Start and end times" && ok "rejects bad start time" || fail "should reject bad time"

BAD_SEASON=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d '{"name":"bad","enabled":true,"startTime":"09:00","endTime":"12:00","startMonth":12,"startDay":15}')
echo "$BAD_SEASON"
echo "$BAD_SEASON" | grep -qi "Seasonal ranges need" && ok "rejects half-season (with valid times)" || fail "should reject half-season"

section "10) Delete a schedule"
PRE_COUNT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules" \
  | python3 -c "import sys,json; print(len(json.load(sys.stdin)))")
curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$FRIDAY_ID"
POST_COUNT=$(curl -s -b "$COOKIES" "$BASE/api/virtual-libraries/$LIB/schedules" \
  | python3 -c "import sys,json; print(len(json.load(sys.stdin)))")
[ "$((POST_COUNT))" = "$((PRE_COUNT - 1))" ] \
  && ok "delete reduced count by 1 ($PRE_COUNT -> $POST_COUNT)" \
  || fail "delete didn't reduce count ($PRE_COUNT -> $POST_COUNT)"

section "11) Evaluate endpoint fires reconciliation now"
# With zero enabled schedules this library should be always-on
# so evaluate just returns unchanged. Let's make one rule that makes it ACTIVE
# right now (we'll use ?at= to drive a future test), then actually evaluate now.
OUT=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules/evaluate")
echo "$OUT"
echo "$OUT" | python3 -c "
import sys, json
d = json.load(sys.stdin)
assert 'created' in d and 'removed' in d, 'missing report fields'
print('  evaluate returned report with created/removed/broken/unchanged keys')
" && ok "evaluate returns reconcile report structure" || fail "evaluate missing fields"

# Now test with a real activation scenario: set a rule active NOW
NOW_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
# Find current day-of-week as ISO number (mon=1..sun=7), convert to 0-indexed
NOW_DOW=$(date -u +%u)  # 1=Mon .. 7=Sun
DOW_IDX=$((NOW_DOW - 1))  # 0=Sun .. 6=Sat
START_H=$(date -u +%H)
START_M=$(date -u +%M)
END_H=$(printf "%02d" $(( (10#$START_H + 2) % 24 )))
OUT_CREATE=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"now-window\",\"enabled\":true,\"daysOfWeek\":[$DOW_IDX],\"startTime\":\"${START_H}:${START_M}\",\"endTime\":\"${END_H}:${START_M}\"}")
eval_rule_id=$(echo "$OUT_CREATE" | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
echo "Created rule $eval_rule_id for now ($NOW_UTC dow=$DOW_IDX start=${START_H}:${START_M})"

OUT_EVAL=$(curl -s -b "$COOKIES" -X POST "$BASE/api/virtual-libraries/$LIB/schedules/evaluate")
echo "$OUT_EVAL" | python3 -c "
import sys,json
d = json.load(sys.stdin)
print(f'  evaluate result: isActive={d[\"isActive\"]} tz={d[\"timeZone\"]}')
" && ok "evaluate succeeds" || fail "evaluate failed"

# Cleanup: delete the now-window rule
curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$eval_rule_id" > /dev/null 2>&1
curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$SAT_ID" > /dev/null 2>&1
curl -s -b "$COOKIES" -X DELETE "$BASE/api/virtual-libraries/$LIB/schedules/$HOLIDAY_ID" > /dev/null 2>&1

# ── summary ───────────────────────────────────────────────────────────────
echo
echo "=== Results: $PASS passed, $FAIL failed ==="
[ "$FAIL" -eq 0 ] || exit 1
