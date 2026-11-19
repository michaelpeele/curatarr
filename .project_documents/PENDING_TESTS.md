# Curatarr — Pending Tests

> **Note:** There is no formal test suite yet. Coverage is ad-hoc via shell scripts
> (`test-api.sh`, `test-membership.sh`, `test-scan-pipeline.sh`) and manual browser testing.

## ✅ High Priority — COMPLETED (2026-09-07)

- [x] **First-Boot Wizard backend e2e (HTTP driver)** — `e2e_bootstrap.py` via cookie jar:
  pre-boot `isBootstrapped=false` → `POST /api/bootstrap` 200 → auto-login → `me`=admin/Admin →
  post-boot `isBootstrapped=true` → library + 2 source roots persisted.
  **8/8 checks PASSED.**
- [x] **Wizard re-entry prevention (Test #2)** — restarted containers; `GET /api/bootstrapped`
  still `true` → main app shown, wizard NOT shown again.
- [x] **Wizard with existing users (Test #3)** — `POST /api/bootstrap` with an existing admin
  returns **400** `{"error":"Admin user already exists. Use the normal login flow."}` —
  one-shot guard holds.
- [x] **SQLite persistence (Test #4)** — after container restart, admin logs in with the
  bootstrap password, `/api/me` returns admin/Admin, 1 library present → all data intact in
  `curatarr_curatarr-data` volume.

### Remaining high-priority

- [ ] **First-Boot Wizard full UI click-through (Test #5)** — CDP-driven regression coverage
  of the 4-step React component (backend flow already proven via HTTP driver).

> **Update 2026-11-14:** the `e2e_bootstrap.py` HTTP driver is gone; backend wizard
> coverage now lives in `test-api.sh` (section 0 + step 10), run against the sandbox
> (`docker-compose.sandbox.yml`). Verified: full suite (fresh), happy path (fresh),
> guard (seeded) — all passed.

## ✅ Folder-Level Symlinks — completed (2026-11-14)

- [x] **Folder-symlink QA** (`/tmp/opencode/qa-symlink.sh`) — bootstrap → login →
  scan → approve one show episode row + movie → reconcile → `/output/Bluey → /media/Shows/Bluey`
  (folder link) + `/output/Arrival (2016)/Arrival.mkv` (per-file link) →
  unapprove + reconcile removes link → re-reconcile idempotent (no-op).
  **ALL CHECKS PASSED.**
- [x] **Movie per-file unchanged** — movie symlinks continue to be per-file, not folder
  (verified in same QA run).
- [x] **Double-approve idempotency** (`/tmp/opencode/qa-idempotent.sh`) — 3× same item:
  all HTTP 200, no unique-index violation. Unapprove: first call 200, subsequent 404.
  **PASSED.**
- [x] **Collision safety** (`/tmp/opencode/qa-collision2.sh`) — two episode rows for
  same show approved → reconcile → single folder symlink, both VLI rows recorded against
  it, no crash. **PASSED.**
- [x] **Broken-source reporting** — delete source file or directory → reconcile
  marks the record as `Broken`, no crash. **PASSED.**

### Remaining high-priority

- [ ] **Folder-Level Symlinks browser validation** — verify in a real browser: one card
  per show, Mark/Unmark (no partial badge), movies unchanged, folder symlinks visible in
  the output directory. **Screenshot automation not possible on this host:** aarch64 + no
  system Chromium, and `playwright install chromium` cannot reach `cdn.playwright.dev`.
  Hand this click-through to Michael at the deployed URL.

## Medium Priority

- [ ] **API endpoint smoke tests** — curl-based checks for each endpoint group:
  - Auth: register → login → me → logout
  - Invite flow: invite → accept → verify membership
  - Source roots: create → list → delete
  - Virtual libraries: create → add item → approve → reconcile
  - Path mappings: create → list → delete
- [ ] **SignalR scan progress** — open app, trigger scan, verify live progress panel updates
- [ ] **Path mapping correctness** — create `/media → /mnt/media` mapping, reconcile,
  verify symlink targets resolved through mapping
- [ ] **Role enforcement** — Viewer cannot approve/unapprove items in a library they
  don't own; Curator can; Admin bypasses

## Low Priority

- [ ] **Drift vs archived original** — confirm `/codebase/Curatarr/` matches
  `/codebase/archive/Curatarr/` for Engine + untouched frontend files
- [ ] **Cross-platform** — tested on Pi (aarch64) only; verify amd64 build works
- [ ] **Doctor agent** — `docker compose --profile doctor up --build -d` boots and can
  run `docker compose -f $COMPOSE_FILE ps`
- [ ] **Unit tests for Engine** — `LibraryScanner`, `SymlinkEngine`, `MetadataProvider`
  (no framework selected yet)

## How to run existing test scripts

```bash
cd /home/michaelpeele/ai-projects/Curatarr
./test-api.sh          # basic API smoke + wizard tests (FRESH instance only -- creates users)
./test-membership.sh   # membership / role tests
./test-scan-pipeline.sh# scan + reconcile pipeline tests

# Sandbox (recommended for test-api.sh): isolated staging stack, never touches prod
./scripts/sandbox-seed.sh                      # reset + seed sandbox DB from prod copy
./scripts/sandbox-seed.sh --null-ratings 1500  # ...+ demo backfill work for filter UI
./scripts/sandbox-seed.sh --no-salvage         # fresh prod clone, no sandbox detail salvage
docker compose -f docker-compose.sandbox.yml up -d
BASE=http://localhost:5001 ./test-api.sh                  # full suite
BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh    # wizard tests only
docker compose -f docker-compose.sandbox.yml down         # stop (add -v to reset DB)
```
