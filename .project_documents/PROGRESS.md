# Curatarr — Progress

## ✅ Group D / D4 — step 6 scratch rehearsal (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, `REMAINING-WORK-GROUPS.md`, and `ROADMAP.md`. User approved step 6
      after item 1; rehearsal was scratch-only.
- [x] Build/sandbox gate passed: `./scripts/promote.sh --sandbox-only` rebuilt
      `api`/`web`, recreated sandbox, smoke passed, and
      `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh` passed.
- [x] `scripts/d4-rehearsal.sh` isolated scratch slots and edge under ignored
      `state/rehearsal/`; public URL `http://localhost:51573`. Snapshot came
      from `curatarr-sandbox-data`, never prod.
- [x] Rehearsal passed: initial active=blue, upstream `127.0.0.1:15173`;
      `--deploy-standby --check`, `--deploy-standby`, `--smoke-standby` (DB
      integrity ok; counts AspNetUsers=3, VirtualLibraries=2, SourceItems=38375
      matched backup), `--flip --confirm-cutover` to green, and
      `--rollback --confirm-cutover` back to blue. Final upstream
      `127.0.0.1:15173`; blue rw, green ro.
- [x] Public checks passed: `/api/bootstrapped`, `/api/me`, SignalR negotiate
      on `/hubs/scan`, `/hubs/activity`, `/hubs/logs`, and SPA
      `/accept-invite?...` / `/reset-password?...`. Bare `/hubs/` is 404;
      real hubs are listed above.
- [x] Write guardrails passed: active slot writable, standby `/output` blocked
      (`blue-write-ok`/`green-write-ok`, `blue-write-blocked`/`green-write-blocked`).
- [x] Teardown removed scratch containers. First down used the default Compose
      project and briefly stopped prod containers. Prod was restored immediately
      with `docker compose -f docker-compose.yml up -d --no-build` and verified
      `prod-api=200`, `prod-web=200`. Root cause fixed with
      `name: curatarr-d4-edge`; re-run left prod containers up.
- [x] No production cutover or prod routing change.
- [ ] Later, after separate approval: production cutover with explicit go/no-go,
      maintenance window, final snapshot, and user-directed flip/rollback.

## ✅ Group D / D4 — steps 4-5 data/auth guardrails (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, `REMAINING-WORK-GROUPS.md`, and `ROADMAP.md`. User approved
      **steps 4-5 only**: data/auth guardrails for D4 slots. Step 6 scratch
      rehearsal is now approved and tracked above.
- [x] Implemented and validated guardrails: slot `/output` mounts default to
      `ro` via `OUTPUT_MOUNT_MODE`; D4 modes set `rw` only on the active slot.
      Standby DB checks use their own restored volume, and restore keeps
      `chown -R 1654:1654 /data` plus `data-protection-keys` copy. Proxy-facing
      slots enforce `COOKIE_NAME=curatarr.auth`. Read-only validation passed:
      `bash -n scripts/promote.sh`, `./scripts/promote.sh --help`, `--status`,
      `--deploy-standby --check`, slot Compose config variants, `git diff --check`.
      No slot started; routing unchanged; production untouched.
- [x] Step 6 scratch rehearsal of deploy/smoke/flip/rollback on scratch ports
      and hostname is done above.
- [ ] Later, after separate approval: production cutover with explicit go/no-go,
      maintenance window, final snapshot, and user-directed flip/rollback.

## ✅ Group D / D4 — step 3 promote modes (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, `REMAINING-WORK-GROUPS.md`, and `ROADMAP.md`. User approved **step 3
      only**: extend `promote.sh` with status/deploy/smoke/flip/rollback modes.
- [x] Added D4 modes to `scripts/promote.sh` while preserving full, sandbox-only,
      prod-only, and seed behavior. New modes log to `logs/promotions.log`; flip
      and rollback require `--confirm-cutover`, and no production cutover was
      performed.
- [x] Validated with `bash -n scripts/promote.sh`, `./scripts/promote.sh --help`,
      `--status`, and `--deploy-standby --check` only; no slots were started,
      routing was unchanged, and production was not touched.
- [ ] Later, after separate approval: production cutover with explicit go/no-go,
      maintenance window, final snapshot, and user-directed flip/rollback.

## ✅ Group D / D4 — step 2 edge proxy and flip/rollback (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, `REMAINING-WORK-GROUPS.md`, and `ROADMAP.md`. User approved **step 2
      only**: host edge proxy/Caddyfile and a flip/rollback mechanism.
- [x] Added `docker-compose.edge.yml`, `configs/edge.env.example`,
      `configs/Caddyfile.template`, and executable `scripts/edge.sh`; added `state/`
      to `.gitignore`. Caddy uses host networking, terminates TLS for
      `CURATARR_DOMAIN`, and forwards to the active slot web port. `scripts/edge.sh`
      provides `status`, `render`, `flip`, `rollback`, `reload`, and `entrypoint`;
      flip/rollback require `--confirm-cutover` and only change active state plus a
      Caddy reload.
- [x] Validated edge files without starting the proxy or changing routing:
      `bash -n scripts/edge.sh` passed; `docker compose -f docker-compose.edge.yml
      --env-file configs/edge.env.example config --quiet` passed; `status` reported
      `active_slot=blue`, `upstream=127.0.0.1:15173`; `render` wrote `state/Caddyfile`
      with `reverse_proxy 127.0.0.1:15173`. No proxy container started and no active
      production routing changed.
- [ ] Later, after separate approval: extend `promote.sh` with status/deploy/smoke/
      flip/rollback modes. Not part of step 2.
- [ ] Later, after separate approval: production cutover with explicit go/no-go,
      maintenance window, final snapshot, and user-directed flip/rollback.

## ✅ Group D / D4 — step 1 slot template (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, `REMAINING-WORK-GROUPS.md`, and `ROADMAP.md`. User approved **step 1
      only**: the parameterised slot Compose template and env examples.
- [x] Added `docker-compose.slot.yml` plus `configs/slot-blue.env.example` and
      `configs/slot-green.env.example`. Slots are real production slots for roughly
      3–4 users; sandbox remains the mandatory first gate. Each slot has its own
      network and internal `curatarr-api` alias because web nginx hardcodes
      `proxy_pass http://curatarr-api:5000`.
- [x] Validated both slot configs with `docker compose --env-file ... config --quiet`
      and confirmed no container, port, volume, or output-path collisions with the
      sandbox stack or current single-stack production layout. Blue resolves to
      `curatarr-blue-api`/`curatarr-blue-web`, ports `15100`/`15173`, volume
      `curatarr-blue-data`, network `curatarr-blue-net`; green resolves to
      `curatarr-green-api`/`curatarr-green-web`, ports `15200`/`15273`, volume
      `curatarr-green-data`, network `curatarr-green-net`. Both keep API alias
      `curatarr-api`.
- [ ] Later, after separate approval: add host edge proxy/Caddyfile and flip/rollback
      mechanism. Not part of step 1.
- [ ] Later, after separate approval: extend `promote.sh` with status/deploy/smoke/
      flip/rollback modes. Not part of step 1.
- [ ] Production cutover is part of D4 but not performed here. It requires explicit
      go/no-go, maintenance window, final snapshot, and user-directed flip/rollback.

## ✅ Group D / D2 — TLS reverse proxy (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this
      section, and `REMAINING-WORK-GROUPS.md`. Scope is documentation plus sandbox
      validation only; production TLS is not applied.
- [x] Added `.project_documents/TLS-REVERSE-PROXY.md` covering Tailscale HTTPS, a
      Caddy + Let's Encrypt example, and that Curatarr stays HTTP behind the proxy.
- [x] Updated `README.md` and docs index/ROADMAP links to point to the TLS guide.
- [x] Validated the guide with a scratch Caddy HTTPS proxy in front of sandbox
      `:5174`. Used a self-signed localhost certificate on host port `18443`.
      `https://localhost:18443/api/bootstrapped` returned `{"isBootstrapped":true}`,
      the page served `assets/index-UEE7VSba.js` and `assets/index-fENfh4zq.css`,
      and login worked. After authentication, `POST
      /hubs/scan/negotiate?negotiateVersion=1` returned 200 with a valid
      connection. Browser console showed only the expected unauthenticated
      `/api/me` 401 on the login screen; no application errors. Scratch container
      and temp directory were removed.
- [x] Marked `ROADMAP.md:627` done only after sandbox validation, noting that prod
      TLS/cutover was intentionally not applied; closeout committed and pushed.

## ✅ V1 release bundle — published-image Compose file (done 2026-11-19)

- [x] Docs-first status set in `PLAN-deployment-operations-hardening.md`, this section,
      and `REMAINING-WORK-GROUPS.md`; all-in-one image logged as a future unchecked
      ROADMAP item, not part of V1.
- [x] Added `docker-compose.release.yml` using the published GHCR images, service name
      `curatarr-api` (required by nginx proxy), one named data volume, optional env
      configuration, and no local `build:` blocks.
- [x] Documented the released install path in `README.md` and added the release tag
      variable to `.env.example`.
- [x] Validated the release Compose file with `docker compose -f
      docker-compose.release.yml config --quiet`; it resolved to
      `ghcr.io/michaelpeele/curatarr-api:1.0.0` and
      `ghcr.io/michaelpeele/curatarr-web:1.0.0`.
- [x] Pushed commit `0c28d00 Release: add published-image compose bundle` and the
      annotated `v1.0.0` tag. The tag triggered workflow run `44449305935`
      (`https://github.com/michaelpeele/curatarr/actions/runs/44449305935`), which
      completed successfully at 2026-11-19T03:09:25Z on head SHA
      `0c28d0066c34be43215f7afb91db82e16c1af6c4`.
- [x] Verified published tags with `docker buildx imagetools inspect`: API and web
      `:1.0.0`, `:1.0`, `:latest`, and `:sha-0c28d00` resolve to the same index
      digests. `:1.0.0` exposes `linux/amd64` and `linux/arm64` for both images.
      API digest `sha256:1ee25c0f6aa5d0b6a00062fd52b9b7980c68fac8ad979946bf7c9ff2c7a7397a`;
      web digest `sha256:45d313b5c2a7d4486624811cf94eb73caa9291fb5c4308065bbdb0c702f7a29c`.
- [x] Pulled both `1.0.0` images and started a scratch copy of the release Compose
      file with project `curatarr-release-check`, a unique data volume, and port
      15003 (production stack and `curatarr-data` volume untouched). API became
      healthy and `/api/bootstrapped` returned `{"isBootstrapped":false}` on the
      fresh DB. Web served `assets/index-UEE7VSba.js` and
      `assets/index-fENfh4zq.css`; the first-boot wizard loaded with zero browser
      console errors. Scratch containers, network, volume, and files were removed.
- [x] No application code changed after the prior promotion, so no rebuild, sandbox
      rerun, or prod promotion was needed. D2, D4, and Multiselect Bug #6 were not
      started; only the future all-in-one image item remains unchecked.

## ✅ Group D / D3 — registry and multi-arch CI (done 2026-11-19)

- [x] Docs-first status set: `PLAN-deployment-operations-hardening.md` marks D3 in
      progress before workflow/README changes.
- [x] Added `.github/workflows/publish-images.yml`, publishing `curatarr-api` and
      `curatarr-web` to GHCR as `linux/amd64` + `linux/arm64` manifests, triggered by
      `v*` tags, published releases, and manual dispatch. Tags are semver,
      `major.minor`, SHA, and `latest` for tag/release runs.
- [x] Documented registry pulls and tag policy in `README.md` without changing local
      `promote.sh` behavior.
- [x] Pushed as commit `d9ad777 CI: publish multi-arch images to GHCR`. Ran the
      workflow via CLI (`gh workflow run publish-images.yml --ref main`); run
      `44442469640` on `main` completed successfully at 2026-11-19T02:20:48Z with
      head SHA `d9ad77722ca610388419627ac0f1430211f7f58f`.
- [x] Verified published manifests with `docker buildx imagetools inspect` on
      `ghcr.io/michaelpeele/curatarr-api:sha-d9ad777` and
      `ghcr.io/michaelpeele/curatarr-web:sha-d9ad777`; both resolve to `linux/amd64`
      and `linux/arm64` in one OCI index. Manual dispatch publishes SHA tags only;
      semver and `latest` remain reserved for tag/release runs.
- [x] Pulled both SHA-tagged images locally. The web image serves
      `assets/index-UEE7VSba.js` / `assets/index-fENfh4zq.css`, matching the current
      prod baseline. No app code changed, so no local promotion was needed.
- [x] Marked `ROADMAP.md` Docker Hub/GHCR and ARM+AMD64 CI items done only after the
      workflow run and manifest checks passed.

## ✅ Group D / D1 — local ops basics (done 2026-11-19)

- [x] Docs-first status set: `PLAN-deployment-operations-hardening.md` marks D1 in
      progress before any D1 files are changed.
- [x] Added explicit prod image tags (`curatarr-api:${CURATARR_VERSION:-latest}`,
      `curatarr-web:${CURATARR_VERSION:-latest}`) while keeping local build blocks.
      Also wired documented `TS_EXTRA_ARGS` into the Tailscale sidecar.
- [x] Added operator-facing `.project_documents/BACKUP-RESTORE.md` and linked it from
      `README.md`, `.project_documents/README.md`, `REMAINING-WORK-GROUPS.md`, and
      the ROADMAP D1 tracker.
- [x] Reconciled README, `.env.example`, and compose variable names/defaults.
- [x] Verification: `docker compose config --quiet` passed; `docker compose build api web`
      passed; `./scripts/promote.sh --sandbox-only` passed at 2026-11-19T01:48:12Z
      (sandbox recreated on :5001/:5174, `sandbox smoke: OK`, WIZARD_ONLY guard
      `isBootstrapped=true`, one-shot bootstrap guard 400 `Admin user already exists`).
      `docker compose config --images` reported `curatarr-api:latest`,
      `curatarr-web:latest`, `tailscale/tailscale:latest`.
- [x] Sandbox manual check: cache-bust load on `:5174` served
      `assets/index-UEE7VSba.js` / `assets/index-fENfh4zq.css`; browser console had
      0 errors and 0 warnings. No app code changed, so bundle hashes are unchanged.
- [x] Backup/restore runbook exercised against the sandbox volume only: online
      SQLite backup written to an ignored `bak-config/promotion/...-sandbox-backup`
      folder; source and restored row counts both `3|2|38375`; `pragma integrity_check`
      returned `ok`; scratch API on port 15001 returned `{"isBootstrapped":true}`;
      scratch container and volume were removed. Production DB was not touched.
      The runbook was updated to `chown -R 1654:1654 /data` after restore because the
      API image runs as non-root uid 1654.
- [x] Final drift cleanup: corrected stale D1 wording in the deployment plan and
      replaced obsolete `MEDIA_ROOT`/`OUTPUT_ROOT`/`WEB_PORT`/`API_PORT`/doctor rows
      in `TECHNICAL.md` with the current compose/env keys.
- [x] Promoted with `scripts/promote.sh --prod-only` at 2026-11-19T01:51:51Z (`prod
      smoke: OK`, completed 01:52:03Z). No build or sandbox rerun after verification;
      no code changed after sandbox verification. Hard-refresh on prod `:5173`
      (`?cb=d1-prod-check`) served `assets/index-UEE7VSba.js` /
      `assets/index-fENfh4zq.css`, unchanged from baseline because no app code changed.
      Prod browser console had 0 errors and 0 warnings. ROADMAP Backup/restore was
      marked done only after promotion; D4 remains pending.

## ✅ Group B follow-up — initial-load overlay and per-card quality (2026-11-19)

- [x] Docs updated first: `PLAN-library-grid-ui-polish.md` status and follow-up text,
  plus this pending section, were written before any code changed. The plan was
  corrected for the user's clarification: two movie files should produce two cards
  with individual quality labels, not one card with a combined marker.
- [x] Initial-load overlay: the paged branch where blank skeleton cards appeared now
  renders `InitialLoadingPlaceholder` — a gray card-area panel with a centered,
  modal-style `Loading library…` status (`role="status"`, `aria-live="polite"`),
  using existing surface/border/text tokens. No placeholder cards are rendered.
  Library-switch refetch shares `loading`; page-append `Loading more…` and the
  non-paged text loading path are unchanged. `LazyMount` and
  `CARD_ACTION_ROW_STYLE` are untouched.
- [x] Quality markers: `parseQualities()` now treats explicit `1080p`/`720p`/`480p`
  (and `2160p`) as authoritative and ignores bare `4k`/`uhd` source tags on those
  files. `qualityLabel()` gives one file's resolution; `movieCopyKey()` groups
  duplicate movie files by a normalized release-folder key so alternate metadata
  titles and `, The` ordering still match. Cards for a multi-file title show their
  own suffix (`— 1080p`, `— 720p`); single-copy titles show no suffix; grouped show
  labels keep `qualitySuffix()` unchanged. No API/state/ROADMAP changes.
- [x] Docker build (no host dotnet): `docker compose build api web` passed after the
  final duplicate-key fix. Final bundles: JS `index-UEE7VSba.js`, CSS
  `index-fENfh4zq.css` (previous promoted: `index-R4dyCEyO.js` /
  `index-fENfh4zq.css`).
- [x] Sandbox verification: `./scripts/promote.sh --sandbox-only` passed at
  2026-11-19T00:29:35Z (sandbox recreated on `:5001`/`:5174`, `sandbox smoke: OK`,
  `WIZARD_ONLY=1 ./test-api.sh` zero-write guard: `isBootstrapped=true` and
  one-shot guard returned 400 'Admin user already exists').
- [x] Manual sandbox verification (`:5174`, admin `admin@example.com`, final
  bundle `index-UEE7VSba.js` / `index-fENfh4zq.css`, cache-bust
  `?cb=20261119finalkey`): console had 0 errors and 0 warnings.
  - Initial load: `main [role=status]` showed `Loading library…`, gray surface
    background, `minHeight: 640px`, and zero card elements — no blank cards.
  - `Charlie Brown Thanksgiving`: two cards —
    `A Charlie Brown Thanksgiving (1973) — 1080p` and
    `A.Charlie.Brown.Thanksgiving.1973.720p.BluRay.x264-aAF — 720p`.
  - `Dark Knight`: two `The Dark Knight (2008) — 1080p` cards and
    `The Dark Knight Rises (2012)` with no suffix; no false `4K / 1080p` marker
    from `1080p.UHD.BluRay`.
- [x] Promotion and bundle check: `./scripts/promote.sh --prod-only` passed at
  2026-11-19T00:31:24Z (`prod smoke: OK`; no build and no sandbox rerun; no code
  changes after sandbox verification). Hard-refreshed prod `:5173` with
  `?cb=20261119followupprod`: served JS `index-UEE7VSba.js` (new — previous
  `index-R4dyCEyO.js`), CSS `index-fENfh4zq.css` (unchanged — same as previous).
  Prod console: 0 errors and 0 warnings.

## ✅ Sprint 6 library-grid UI polish — Group B (2026-11-19)

- [x] Docs updated first: `.project_documents/ROADMAP.md` (stale Calendar/Activity
  duplicates marked `[x]` as completed duplicates), `REMAINING-WORK-GROUPS.md`
  (corrected refs, Group B set as next), and new
  `PLAN-library-grid-ui-polish.md` (proposed, then refined after user decisions).
  User retired both mobile and tablet poster-column changes; retained work is the
  shared viewport hook, 44px touch targets, full-screen touch modals, and compact
  tablet sidebar. ROADMAP Group B items were NOT checked off before verification.
- [x] Item 1 (`ROADMAP.md:564`): animated `Loading library…` / `Loading more…`
  indicators with `role="status"`, `aria-live="polite"`, pulse keyframes and a
  `prefers-reduced-motion` guard. Applied to initial load, library switch, page
  append, and `FirstPageSkeleton`; no layout shift, `LazyMount` untouched.
- [x] Item 2 (`ROADMAP.md:607`): client-only quality parser derived from filenames;
  bare marketing `4k` is ignored when an explicit resolution token is present,
  `uhd` maps to 4K. Movie copies are unioned by title/year and show episodes by
  group; suffix ` — 4K / 1080p` appears only for multiple distinct qualities. No
  API, migration, or new quality column.
- [x] Item 3 (`ROADMAP.md:176`, narrowed): new `src/lib/useViewport.ts` replaces four
  duplicated resize listeners. Mobile 3-column grid and tablet
  `repeat(auto-fill, minmax(150px, 1fr))` columns are UNCHANGED. DetailModal and
  MatchModal are full-screen on compact viewports; 44px touch targets added to
  sidebar hamburger, card action buttons (height on mobile, full on tablet), and
  modal controls; tablet sidebar is compact.
- [x] Docker build (no host dotnet): `docker compose build api web` passed after the
  final CSS touch-target fix. Final bundles: JS `index-R4dyCEyO.js`, CSS
  `index-fENfh4zq.css` (previous prod: `index-C4TfM9WN.js` / `index-CoR6XILo.css`).
- [x] Sandbox verification: `./scripts/promote.sh --sandbox-only` passed at
  2026-11-19T00:07:17Z (sandbox recreated, `:5001` smoke OK, `WIZARD_ONLY=1
  ./test-api.sh` zero-write guard PASS with one-shot bootstrap guard returning 400).
- [x] Manual sandbox verification (`:5174/?cb=20261119b` and `?cb=20261119c`, admin
  `admin@example.com`, 2026-11-19): served assets confirmed
  `index-R4dyCEyO.js` / `index-fENfh4zq.css`. Quality suffix appears only for
  `The Dark Knight (2008) — 4K / 1080p`; one true multi-copy title across all 2523
  movie entries, no marketing false positives. Animated loading observed on initial
  load and page append; reduced-motion emulation gave `animationName: none`.
  Responsive: 320/375/576px keep 3 columns with no clipped posters; hamburger and
  modal close are 44×44; modals full-screen; 768px compact sidebar with 44×44 card
  actions; 1024px desktop restored. Pre-existing BottomBar overflow at 320px
  (Sync/Schedule offscreen) was noted and left unchanged.
- [x] Calendar slice 2 regression: `Sync applies to all libraries.` visible; All view
  enables Media type, disables Scan/Schedule, keeps Sync enabled; ‹/› arrows and
  date-jump (`2027-03-01`) work; media-type select is locked for concrete libraries.
  All-view event cards open the modal via native buttons/keyboard; modal starts at
  `Choose a library…` with disabled actions, exact PosterGrid wording and Sync
  notes. Hide on a show event POSTed all 34 `sourceItemIds`; Viewer account saw the
  read-only no-writable-library message. No concrete library has dated events in
  the sandbox in 2025-01..2027-12, so read-only concrete cards were verified in code
  (`clickable = libraryFilter === ""`) rather than by click.
- [x] Final browser console after fresh cache-busted load and all interactions:
  0 errors, 0 warnings.
- [x] Promotion (`./scripts/promote.sh --prod-only` at 2026-11-19T00:15:23Z; no code
  changed after sandbox verification; prod smoke OK). Hard-refreshed prod `:5173`
  with `?cb=20261119prod` and confirmed served JS `index-R4dyCEyO.js` (new —
  previous was `index-C4TfM9WN.js`) and CSS `index-fENfh4zq.css` (new — previous was
  `index-CoR6XILo.css`). Prod Calendar loads with `Sync applies to all libraries.`
  visible and zero console errors. ROADMAP Group B items may now be marked done.

## ✅ Calendar slice 2 — All-view link/Hide modal (done 2026-11-18)

- [x] Docs updated first: `.project_documents/PROMPT-calendar-slice2.md`,
  `PLAN-calendar-from-metadata.md`, `REMAINING-WORK-GROUPS.md` mark slice 2 in progress;
  stale plan response fields/props, admin-only settings reference, and bundle hash reconciled.
  ROADMAP items were checked off only after sandbox verification and prod promotion.
- [x] Backend: extend `GET /api/calendar/events` events with `sourceItemIds` and
  `representativeSourceItemId`. Shows group all SourceItems for the de-duplicated
  `(MediaType, TmdbId, date)` event with the metadata-bearing representative first;
  movies emit a single id. API probe confirmed "Dexter: Resurrection" (2026-11-15,
  tmdbId 259909) carries 34 sourceItemIds with a representative row first.
- [x] Web: All-view clickable event cards + `CalendarItemModal` reusing existing
  approve/hide APIs, PosterGrid wording, and per-library state loading.
- [x] Docker build + sandbox verification (`./scripts/promote.sh --sandbox-only`
  passed at 2026-11-18T17:54Z; `:5001` smoke OK, zero-write guard + one-shot
  bootstrap guard PASS, `WIZARD_ONLY=1 ./test-api.sh` stopped after section 0)
  and manual sandbox checks (`:5174`, 2026-11-18, admin admin@example.com):
  - All-view event cards are native `<button>`s with aria-labels; keyboard focus +
    Enter opened the modal. Concrete-library cards remain read-only `<div>`s with
    no aria-label/role and clicking opens no modal (verified with a temporarily
    approved "Hunted" item in the TV library, then unapproved).
  - Modal starts at `Choose a library…` with both actions disabled; selecting a
    writable library enables them and loads state via GET `/approved-item-ids` +
    `/hidden-items` only. Admin saw both sandbox libraries in the picker. Viewer
    path verified in-browser with `test-user@example.com` / `Passw0rd!123`: the Calendar
    library selector offered only `All libraries`, clicking a card opened the modal,
    and it showed only `You do not have a library you can change. Ask an
    administrator for access.` (no picker, no actions). Owner/Curator allowance is
    implemented per code (no Owner/Curator test user exists in the sandbox).
  - Mark/hide verified against existing APIs with exact PosterGrid wording and Sync
    warnings: POST `/approve` used only the representative id → `Marked — click Sync
    to add to the library; click to unmark` + toast `"Dexter: Resurrection" added to
    library`; hide POST carried all 34 ids → `Hide requested — click Sync to apply;
    click to cancel` + toast `34 hides requested — press Sync to apply`; cancel
    DELETE restored `Visible in this library — click to hide`. Marking an already
    marked item used `In this library — click to unmark`; hiding a marked item showed
    the exact `Choosing Hidden removes this title from the Library…Sync…Plex…`
    confirmation and unapproved + hid all ids. The complementary `Choosing Library
    removes this title from Hidden…Sync…Plex…` confirmation is implemented.
  - `Sync applies to all libraries.` visible; in All view Sync enabled and
    Scan/Schedule disabled; concrete library locks/disables the media-type select.
    ‹/› month arrows and date-jump (`2026-12-01` → December 2026) work; BottomBar
    intact. Browser console: 0 errors, 0 warnings.
  - Build output: JS `index-C4TfM9WN.js` (new), CSS `index-CoR6XILo.css` (unchanged —
    no stylesheet files edited; styling is inline).
- [x] Promotion (`./scripts/promote.sh --prod-only` at 2026-11-18T18:00Z; no code
  changed after sandbox verification; prod smoke OK). Hard-refreshed prod `:5173`
  and confirmed served JS `index-C4TfM9WN.js` (new — previous was `index-WyeMlDms.js`)
  and CSS `index-CoR6XILo.css` (unchanged — no stylesheet files edited; all new
  styling is inline). Prod Calendar loads with `Sync applies to all libraries.`
  visible and zero console errors. ROADMAP Sprint 4 #2 Calendar items can now be
  checked off.

## ✅ Done

### Calendar from metadata — read-only slice 1 (2026-11-18)
- [x] Plan/docs updated first: `.project_documents/PLAN-calendar-from-metadata.md`,
  `REMAINING-WORK-GROUPS.md`, `ROADMAP.md` pointers.
- [x] Backend: explicit `SourceItem` date columns + migration/Designer/snapshot;
  cached-JSON date parser; startup sweep for already-cached rows;
  `GET /api/calendar/events` with server-timezone month/today, media-type and
  library filters, membership read gate, approval/effective-hidden filtering, and
  show de-duplication.
- [x] Web: new `CalendarPage` (month grid, arrows, iOS-friendly date-jump heading,
  poster/title cells, New Season badge, library/type filters), sidebar Soon flag
  removed, shared BottomBar Scan/Sync preserved; no link/hide controls (slice 2).
- [x] Docker build passed: `curatarr-api:latest` / `curatarr-web:latest`; JS
  `index-CvyC6Suc.js`, CSS `index-CoR6XILo.css`.
- [x] Sandbox-only verification passed (`:5001` smoke + `WIZARD_ONLY=1 ./test-api.sh`).
- Manual browser verification in sandbox (`:5174`, 2026-11-18): no Calendar Soon
  badge; month defaults to November 2026 from server UTC and today 2026-11-18;
  ‹/› and date-jump work; All libraries + All types shows 24 dated show cards with
  posters/titles; New Season badges render (Percy Jackson 3, Yellowjackets 4,
  The Hunting Wives 2); library/type filters work; shared Scan/Sync toolbar
  present; no link/hide controls yet; zero console errors. API spot checks:
  invalid month 400, Movie/Show/Anime filters return 0/24/0 for this sandbox data.
- [x] Promoted to prod with `./scripts/promote.sh --prod-only` at 2026-11-18T15:09Z
  (no code changes after sandbox verification; prod smoke OK). Cache-busted prod
  load served `index-CvyC6Suc.js` / `index-CoR6XILo.css` as expected.

### Calendar slice 1 follow-up — All libraries / Sync / media lock (2026-11-18)
- [x] Decision documented first in `.project_documents/CALENDAR-SLICE1-FOLLOWUP-UI-FLOW.md`:
  keep Sync enabled with an explicit note; selectable media type switches to, and is
  locked to, the selected library's type.
- [x] Web: Calendar `All libraries` is now a valid shared selection in both the page
  filter and BottomBar; intentional `null` survives library reloads/Sync; Scan and
  Schedule are disabled with single-library titles; Sync is enabled with
  `Sync applies to all libraries.` shown on the Calendar page.
- [x] Web: choosing a concrete library switches the media filter to that library's
  type and disables the media-type select with `Locked to this library's media type`;
  returning to `All libraries` unlocks it and resets to All types.
- [x] Docker build passed: JS `index-WyeMlDms.js`, CSS `index-CoR6XILo.css`.
- [x] Sandbox-only verification passed (`:5001` smoke + `WIZARD_ONLY=1 ./test-api.sh`).
- Manual browser verification in sandbox (`:5174`, 2026-11-18, cache-busted):
  `All libraries` appears in both selectors; selecting it unlocks/reset media type to
  All types and shows 24 dated cards; Scan/Schedule disabled with new titles while
  Sync remains enabled with original Sync warning; clicking Sync completed
  (`POST /api/reconcile` 200) and All selection survived the library reload;
  selecting Movies library locked media to Movie, selecting TV Shows locked it to
  Show, and changing the shared BottomBar selector synced the Calendar filters;
  ‹/› and date-jump still work; zero console errors.
- [x] Promoted to prod with `./scripts/promote.sh --prod-only` at 2026-11-18T16:05Z
  (no code changes after sandbox verification; prod smoke OK). Cache-busted prod
  load served `index-WyeMlDms.js` / `index-CoR6XILo.css` as expected.

### Dockerization
- [x] Multi-stage API Dockerfile (SDK → ASP.NET runtime)
- [x] Multi-stage Web Dockerfile (Node build → nginx)
- [x] Docker Compose with API, Web, Tailscale, Doctor profiles
- [x] .env.example with all configurable variables

### Fixes Applied (2026-09-20)
- [x] Fixed nginx.conf directory issue (was accidentally created as directory)
- [x] Added healthcheck to API service
- [x] Fixed volume mount paths in docker-compose.yml
- [x] Added network aliases for proper DNS resolution
- [x] Fixed depends_on to use condition: service_healthy
- [x] Fixed ts-curatarr container name typo
- [x] Web container now running on port 5173 using Docker volumes
- [x] nginx.conf mount issue resolved using conf.d volume
- [x] Frontend serving correctly at http://localhost:5173

### Folder-Level Symlinks (2026-11-14)
- [x] `Curatarr.Engine/SymlinkEngine.cs` — `ResolveLinkPaths`: shows/anime use a
  single folder symlink **flattened to the library root** (letter-group source
  containers like `M thru Z` dropped from the output path; `ShowTitle` segment is
  the authoritative show root); movies unchanged (per-file)
- [x] `SymlinkEngine` — `SourcePathExists`, `IsSymlink` (directory-symlink-aware),
  `DeleteLink` (handles dir symlinks), collision-safe link creation (if link already
  exists with correct target, just record the VLI against it)
- [x] `Curatarr.Api/Program.cs` — approve endpoint made idempotent (skips duplicate);
  two new unique indexes seeded: `IX_SymlinkRecords_UniqueLinkPerLibrary`,
  `IX_VirtualLibraryItems_OnePerLibraryAndItem`
- [x] `Curatarr.Api/BulkApprovalEndpoints.cs` — deleted; bulk endpoints removed
- [x] `client.ts` — `approveMany`/`unapproveMany` removed; `SourceItem.relativePath` added
- [x] `showGroups.ts` — `representativeId(group)`: first episode row with a poster;
  `episodeKey` path fallback; season-0 exclusion from `seasonCount`
- [x] `PosterGrid.tsx` — `handleApproveShow`/`handleUnapproveShow` use single-row
  `api.approve`/`api.unapprove` on `representativeId`; partial badge removed;
  `isApproved = approvedIds.has(group.representativeId)`
- [x] `DetailModal.tsx` — show summary line (episode/season/latest)
- [x] Typecheck + production Vite build pass (`index-D5tOK1C1.js`, 300.64 kB)
- [x] API Docker image built (`curatarr-api:folder-level-symlinks`)
- [x] Web Docker image built (`curatarr-web:folder-level-symlinks`)
- [x] QA: folder symlink created (`/output/Bluey → /media/Shows/Bluey`); movie
  per-file unchanged; re-reconcile idempotent; unapprove removes link; collision-safe
  (two episode rows for same show → single link, no 500); double-approve idempotent
  (3× HTTP 200); double-unapprove 404 on subsequent calls

### Re-invite Bug (2026-11-14)
- [x] `Curatarr.Api/Program.cs` `/api/users/invite` — orphan-check via `FindByEmailAsync`
  before `CreateAsync`: pending invites (`EmailConfirmed=false`, no password) get a fresh
  link **resent (200)** instead of a duplicate-row 400; active accounts get a friendly 400;
  `db.ChangeTracker.Clear()` before create; role-assign/email-send failures roll the new
  row back (no new orphans); `Console.WriteLine` → structured `ILogger` on all paths
- [x] `UsersPage.tsx` + `client.ts` — invite response carries `message`, UI shows
  "Invite re-sent to ..." vs "Invite sent to ..."
- [x] `test-api.sh` steps 7–9 — fresh invite 200, re-invite pending → 200 "re-sent",
  active account → friendly 400
- Verified live: 5/5 curl scenarios pass (fresh, re-invite, active-400, delete→re-invite,
  idempotent DB state: 1 user + 1 role row after cleanup)

### Real Email Sending (2026-11-14)
- [x] `Curatarr.Api/EmailSender.cs` — new `SmtpEmailSender` (`System.Net.Mail.SmtpClient`,
  no new NuGet deps): reads `Smtp__Host/Port/User/Password/From/EnableSsl` env, STARTTLS,
  never logs the password; `ConsoleEmailSender` kept as fallback
- [x] `Program.cs` — conditional DI: `Smtp__Host` set → SMTP sender + startup log line
  ("SMTP mode via smtp.gmail.com:587"); empty → console fallback log line
- [x] `forgot-password` hardened — SMTP failure logged via `ILogger`, still returns 200
  (anti-probing guarantee preserved); invite path already rolls back on send failure
- [x] `docker-compose.yml` + `.env` + `.env.example` — `Smtp__*` passthrough; Gmail app
  password configured (sender `admin@example.com`)
- Verified live: reset email + invite email both accepted by smtp.gmail.com:587
  (`[Email] Sent ...` in `docker logs`); QA invite user cleaned up (DB back to 1 user)
- **`FRONTEND_BASE_URL` switched to the Tailscale Funnel URL**
  (`https://curatarr.tail0307d.ts.net`) — email links (invite/reset/confirm-email-change)
  previously pointed at `localhost:5173`, which only resolves on the Pi itself
- **Browser-validated by Michael 2026-11-14:** clicked the emailed invite link → funnel
  URL loaded the Curatarr account setup page; QA invite user then deleted (DB: 1 user)

### Sandbox / Staging Stack + Wizard API Tests (2026-11-14)
- [x] `docker-compose.sandbox.yml` — isolated staging stack: own project name
  (`curatarr-sandbox`), containers, network, ports (API 5001 / web 5174), volume
  (`curatarr-sandbox-data`); no Tailscale; console email; `/output` on an isolated host
  dir so a sandbox reconcile can never touch prod's VirtualLibraries; network alias
  `curatarr-api` so the web image's hardcoded `proxy_pass http://curatarr-api:5000`
  resolves (verified: `localhost:5174/api/bootstrapped` works)
- [x] `test-api.sh` — `BASE=` override, `WIZARD_ONLY=1` mode, section 0
  (`isBootstrapped` state check; fresh DB → bootstrap happy path + login + Admin role;
  bootstrapped DB → guard only), step 10 (state transition + one-shot 400 guard)
- [x] Seeding workflow formalized into **`scripts/sandbox-seed.sh`** (2026-11-15):
  `./scripts/sandbox-seed.sh [--null-ratings N] [--no-salvage]` — stops the sandbox
  stack, copies the prod DB (db+wal+shm) as the base, verifies integrity, salvages
  the sandbox volume's SourceItem detail columns row-by-row (tolerates WAL
  corruption from live copies), fixes ownership to uid 1654 (the non-root user the
  api image runs as — the volume would otherwise be "readonly" to the app), and
  optionally nulls N random `Rating` rows for exercising the filter-status UI.
  Verified end-to-end 2026-11-15 (38,367 SourceItems, integrity ok, sandbox boots
  and serves). Replaces the ad-hoc copy commands that previously lived only in
  `docker-compose.sandbox.yml`'s header comment.
- Verified 3 runs: **full suite fresh** (all sections PASS), **WIZARD_ONLY fresh**
  (happy path + guard PASS), **WIZARD_ONLY seeded** (guard-only PASS, prod untouched)

## ✅ Completed

### Part A: People fields (Director/Creators/Cast) + raw TMDB staging table (2026-11-15)
- [x] **Schema:** 3 new `SourceItem` columns — `Director` (movies, single name),
  `Creators` (shows/anime, `|`-separated), `Cast` (both, top 30 names in TMDB billing
  order, `|`-separated). Same sentinel semantics as the existing detail columns
  (`null` = not fetched → retry, `""` = fetched, none found → never retried);
  cross-kind columns stay `null` forever and are never checked as pending.
  Added to prod DB via raw `ALTER TABLE` (API stopped) alongside `CREATE TABLE
  IF NOT EXISTS TmdbRawDetails` — one downtime window for both.
- [x] **Raw staging table `TmdbRawDetails`** (`Key` = `{mediaType}:{tmdbId}` PK,
  `Json` = full raw TMDB response, `RequestedWith` = append_to_response set used,
  `FetchedAt`) — one row per unique title (2,460 for this catalog: 1,818 movies +
  642 shows), NOT per SourceItem row, so all episodes of a show share one cached
  response. Future field expansions (budget, tagline, runtime, writers...) parse
  locally from here — zero TMDB calls; `RequestedWith` says which keys need a
  targeted refetch if a future field lives in an endpoint we didn't append.
  ~200 MB expected on 514 GB free disk (39 MB DB today).
- [x] **Provider:** `GetCatalogDetailsAsync` now `append_to_response=release_dates,credits`
  (movies) / `content_ratings,credits` (shows) — still ONE round-trip per title;
  extracts director from `Credits.Crew` (`Job=="Director"`), creators from
  `created_by`, top-30 cast (billing order, trimmed + deduped). Raw body captured
  via new `GetRawOrThrowAsync` (shared error handling with `GetJsonOrThrowAsync`,
  Web-defaults JSON options preserved) and rides `CatalogDetails.RawJson`.
  Detail modal unchanged (live top-6 fetch for display).
- [x] **Backfill:** `DetailsBackfill.Pending` adds `Director` (movies) / `Creators`
  (shows) / `Cast` (all); `RunBatchAsync` writes all under per-column `if (is null)`
  guards + stashes raw JSON per batch via atomic SQLite `INSERT ... ON CONFLICT DO
  UPDATE` (the earlier `FindAsync`+`Add` version raced UNIQUE when the hourly
  service and the endpoint ran concurrently — crashed the service run; fixed).
- [x] **API/UI:** `/api/source-items` exposes `director`/`creators`/`cast`;
  `client.ts` `SourceItem` fields; `showGroups.ts` aggregates both (first-non-empty);
  `PosterGrid.needsDetails()` includes them so the `Loading... x / y` status covers
  the new pass; SignalR merged-item literal updated.
- [x] **Backfill verification (2026-11-15):** catalog-wide pass complete — 38,212
  matchable rows done, 0 failed; `Cast`/`Director`/`Creators` populated (cast 36,693,
  directors 1,825 movies, creators 30,933 show/episode rows in the fresh sandbox
  copy); `TmdbRawDetails` holds ~2,460 unique titles with full raw JSON (budget,
  imdb_id, etc. verified). 155 rows (`TmdbId IS NULL` scene rips/samples) are
  permanently unmatchable and excluded by the server's `Pending` predicate — by
  design. Sandbox re-seeded from prod (`sandbox-seed.sh --no-salvage`) + new images;
  `test-api.sh WIZARD_ONLY=1` passes (one-shot guard holds; the full suite needs a
  fresh/empty DB per its own docs — seeded DB is bootstrapped so new registrations
  are Viewers). Browser check: no phantom status on Genres/Ratings/Studios on prod
  OR sandbox; real backfill work still shows `Loading... x / y` → auto-hide.

### Part B: Free-text search over title + people (2026-11-15)
- [x] **Frontend-only (no API changes):** `searchQuery` state + `searchTokens` memo
  (lowercase, whitespace-split, AND-ed); each token is a raw case-insensitive
  substring match (implicit wildcards). `entryMatchesFilters` applies the search
  clause first, then the dropdown predicates, so search ANDs with genre/rating/
  studio/network.
- [x] **Haystacks:** movie → title + director + cast; show → show title + creators +
  cast (`showGroups` aggregates both); overview deliberately excluded. The
  `Map<RenderEntry, string>` is memoized and only built while a search is active, so
  keystrokes don't rebuild it.
- [x] **Toolbar:** `<input type="search">` between the last dropdown (Studio/Network)
  and Clear filters (`Search titles, cast, crew…`); focus fires `ensureDetails()` so
  people columns backfill if the catalog predates Part A. Clear filters now clears
  `searchQuery` too, and search-only input makes the button appear (`anyFilterActive`
  includes `searchActive`). Search resets on library/media-type switch. Empty result
  → `No titles match “…” Try fewer or different words.`
- [x] **Verified (Playwright, prod 5173):** `nolan` → 14 movies (genuine
  director/cast hits incl. Christopher Nolan, Charles Noland); `christopher dark` →
  The Dark Knight ×2 + The Dark Knight Rises + Dark Shadows (AND semantics; the last
  via cast Christopher Lee); `prosecution` (overview-only) → empty + message; Clear
  filters restored all 1,838 cards. Show library: `vince` → 9 shows (Vince Vaughn,
  Vincent D'Onofrio…), `vince` + Comedy → 4 (Bad Monkey), `vince` + Action → 0.
  `npx tsc --noEmit` clean; web image rebuilt + prod/sandbox restarted.

### Small items: log timestamps + real EF Core migrations (2026-11-15)
- [x] **ISO 8601 log timestamps:** `builder.Logging.ClearProviders() +
  AddSimpleConsole(o => o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz ")`, so request,
  EF, Identity, SignalR and hosted-service lines all carry local time + UTC offset.
  Startup diagnostics that run before the host (TMDB key mask, email mode) go through a
  `LogStartup` helper with the same stamp. Verified in `docker logs curatarr-api` (startup
  lines + request lines both stamped). Host-lifetime lines ("Now listening on…") come from
  the console lifetime and remain unstamped.
- [x] **EF Core migrations:** generated `20261115123142_InitialCreate` (437 lines, all
  tables/FKs/indexes incl. `Director`/`Creators`/`Cast`, `TmdbRawDetails`, `PathMappings`);
  the two manually-created unique indexes moved into `OnModelCreating` so new DBs match
  prod. `SeedAsync` now runs `EnsureMigrationBaselineAsync` + `MigrateAsync` instead of
  `EnsureCreatedAsync` + raw `CREATE TABLE` patches. The baseline helper no-ops on fresh
  DBs and, when `SourceItems` already exists, creates `__EFMigrationsHistory` and inserts
  the initial row so nothing is replayed.
- [x] **Verified:** prod DB backed up first to
  `bak-config/promotion/20261115-123211-ef-migrations/` (integrity ok); prod deploy
  baselined cleanly (`__EFMigrationsHistory` → InitialCreate, 82,490 items, 1 user, 8
  approvals, integrity ok, sessions still valid); sandbox clone baselined with 38,367
  items; throwaway fresh volume ran `Applying migration 'InitialCreate'`, exposed all 15
  tables + 19 `SourceItems` columns, and passed the full `test-api.sh`; `dotnet ef
  migrations has-pending-model-changes` → none.

### Sprint 4 item 2: access-control hardening (2026-11-16)
- [x] **Backend audit:** all configuration endpoints already Admin-gated — source-roots
  CRUD, path-mappings, filesystem browse, virtual-libraries list/create/edit/delete +
  membership management, users, scan/pause/resume/stop, reconcile, backfill, schedules
  settings. Content approval (`approve`/`unapprove`) membership-gated per library:
  Admins bypass, Owner/Curator need a row on THAT library, Viewers rejected.
- [x] **Frontend guard:** `App.tsx` — non-admins on any view outside
  `library`/`account`/`calendar` are reset to the library view (covers demotion
  mid-session + stale state; Sidebar already hides admin entries).
- [x] **Verified on sandbox:** Viewer → 403 on source-roots/path-mappings/virtual-libraries/
  users (GET + POST), filesystem browse, scan, reconcile. Curator → approve own library
  200, approve other library 403, list-all-libraries 403.

### Filter bar visibility + persistent sessions (2026-11-15)
- [x] **Sticky filter bar with hide toggle:** the Library toolbar now sticks to the top
  of the scroll area (`position: sticky`, opaque page background, `z-index: 10`) so it
  stays on screen while browsing long lists. Grid wrapper pulled into main's 24px padding
  band and toolbar `top: -1.5rem` so the bar sits flush at y=0 and cards can't peek above
  it. Visible by default; `Hide filters` collapses it to `Show filters` (plus
  `(n active)` when genre/rating/studio/network/search are set) and closes any open
  dropdown. Hiding preserves every selection/search — only the chrome disappears.
- [x] **Why 401s happened:** the auth cookie used .NET's default 14-day
  `.AspNetCore.Identity.Application` name and the Data Protection key ring lived at
  `/home/app/.aspnet/DataProtection-Keys` (container filesystem) — every
  rebuild/recreate rotated keys and silently invalidated all existing cookies. `docker
  exec` confirmed the old key ring location and `Set-Cookie` showed the 14-day expiry.
- [x] **Fix (API):** `AddDataProtection().PersistKeysToFileSystem(<contentroot>/
  data-protection-keys).SetApplicationName("Curatarr")` (→ `/app/data/...`, in the data
  volume); cookie renamed `curatarr.auth`, `ExpireTimeSpan = 30 days`, sliding
  expiration, HttpOnly.
- [x] **Fix (frontend):** `client.ts` exposes `setUnauthorizedHandler`; any 401 from a
  non-`/api/auth/*` request fires it. `App.tsx` registers it only while signed in
  (cold-open `api.me()` 401s stay silent) → drops `me`, renders the login screen with a
  `Your session expired. Please sign in again.` notice; cleared on next successful
  login/sign-out. `handleLogout` now tolerates an already-expired session.
- [x] **Sandbox cookie isolation:** cookie name is configurable (`Cookie__Name`);
  `docker-compose.sandbox.yml` sets `curatarr.sandbox.auth`. Cookies ignore ports, so a
  shared name on localhost meant logging into the sandbox silently signed you out of
  prod (observed while testing).
- [x] **Verified:** login returns `curatarr.auth` with `expires=Tue, 15 Dec 2026` (30d);
  key file created under `/app/data/data-protection-keys`; `docker compose up -d
  --force-recreate api` then the *same* cookie still returned `200` on `/api/me`
  (one key, no rotation). Playwright: cleared cookies mid-session → next API call
  dropped to the login screen with the expiry notice. Sticky bar verified on prod and
  sandbox: `top: 0` at scrollTop 2500, opaque background; Hide → `Show filters (1
  active)` with search still filtering; reload → visible again with reset controls.

### Scheduling engine — Slice 1: model + API + worker (2026-11-15)
- [x] **Model:** `Curatarr.Engine/Data/Models.cs` adds `LibrarySchedule` (Id / VirtualLibraryId FK cascade-delete / Name nullable / Enabled default true / DaysOfWeek comma-separated int string nullable / StartTime & EndTime TimeOnly nullable / optional StartMonth/StartDay/EndMonth/EndDay for seasonal coverage / CreatedAt/UpdatedAt UTC).
- [x] **Migration:** `Curatarr.Engine/Data/Migrations/20261115141233_AddLibrarySchedulesTable.cs` created via `dotnet ef migrations add`; `SeedAsync` now calls `Database.MigrateAsync()` (baseline helper skips when `__EFMigrationsHistory` missing but table exists, so prod with raw-ALTER schema boots cleanly). Applied to prod — 0 rows, integrity ok.
- [x] **Pure engine:** `Curatarr.Engine/ScheduleEngine.cs` static class — `IsActive` (union of enabled rules, fail-open when none), `NextTransition` (minute-resolution candidate sweep up to 366 days), `Validate`, `NormalizeDays`. Overnight windows (`end < start`) cross midnight; `end == start` means all-day; day-of-week applies to the day the window *starts*; seasonal wrap-across-year supported.
- [x] **Reconcile gate:** existing `ReconcileGate` (`SemaphoreSlim(1,1)`) shared between manual `/api/reconcile` and scheduled evaluations; `SymlinkEngine.ReconcileLibraryAsync(libraryId, libraryActive)` reuses existing `ReconcileCoreAsync` scoped to one library.
- [x] **Worker:** `Curatarr.Api/LibraryScheduleService.cs` — `BackgroundService`, 15 s startup delay then every 60 s; evaluates each library, reconciles only on state flip, logs transition.
- [x] **API endpoints** (`Curatarr.Api/ScheduleEndpoints.cs`, admin-only): `POST/GET/PUT/DELETE /api/virtual-libraries/{id}/schedules/{scheduleId}` (create/get/update/delete, validates + normalizes, returns DTO); `GET .../schedules/status?at=<ISO>` (deterministic test hook, defaults to now); `POST .../schedules/evaluate` (fire reconcile now, returns report). Timezone read from `appsettings.json` → `Schedules:TimeZone` (env-overridable via `Schedules__TimeZone`, defaults to server local via `TimeZoneInfo.Local`).
- [x] **Tests:** 47 unit tests in `Curatarr.Api.Tests/ScheduleEngineTests.cs` covering always-on default, single-rule weekday/time, overnight cross-midnight, seasonal wrap Dec→Jan, multi-rule union, disabled ignored, invalid payload rejection, deterministic `?at=` override, gate serialization, null-field handling — all green. `test-schedules.sh` smoke-tests CRUD + status boundary assertions against sandbox.
- [x] **Verified:** prod DB migrated clean; sandbox end-to-end CRUD/status-with-`?at=`/evaluate round-trip; `dotnet ef migrations has-pending-model-changes` → none.

### Scheduling Slice 2: Settings → Server timezone picker (2026-11-16)
- [x] **UI:** `Curatarr.Web/src/components/ServerPage.tsx` — admin-only Settings → Server view with
  a schedule timezone dropdown listing `TimeZoneInfo.GetSystemTimeZones()`, current zone, the
  server's current time in that zone, and whether it is configured.
- [x] **API:** `GET/PUT /api/schedules/settings` in `Curatarr.Api/ScheduleEndpoints.cs` — validates
  the zone against the system list and persists it to `schedules-config.json` in the content root
  (loaded at startup in `Program.cs`); `LogTimeZone` singleton reloaded on PUT.
- [x] **Wiring:** sidebar `settings-server` entry + `App.tsx` route (admin-only).
- [x] **Verified:** deployed and exercised on prod; zone is now `America/New_York`. The schedule
  engine reads the zone at startup, so changing it requires an API restart.
- [x] **Logging note:** custom `ZoneConsoleFormatter` was abandoned (incompatible with the .NET 10
  runtime). Plain `AddSimpleConsole` with `TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz "` — log
  timestamps stay UTC.

### Scheduling Slice 3: Schedule modal + library status text (2026-11-16)
- [x] **Modal:** `Curatarr.Web/src/components/ScheduleModal.tsx` exports
  `ScheduleModal({libraryId, libraryName, onClose})` and `formatStatus(st)`. First page is a
  two-button chooser — **Daily** / **Seasonal** — with no pickers until a mode is chosen; each mode
  page has a `Back` button, and Close/Cancel + Save appear only after choosing.
  - Daily: 7 weekday rows (checkbox + day label + custom `TimeRangeField`). Safari renders no picker
    for `<input type="time">`, so the control is a styled text box (`6:30 PM – 8:00 PM` or
    `pick a time`) that opens a popover with Hour/Minute selects, an AM/PM toggle and
    `Next`/`Done`; committing the start auto-opens the end pre-filled start + 2 h. The row checkbox
    auto-ticks once both times are set; blank/equal times mean the whole day (`00:00`–`00:00`). One
    rule per checked day, named `"{DAY} window"`, `daysOfWeek: [day]`.
  - Seasonal: native `<input type="date">` from/to pairs with `+`/`−` row controls (remove disabled
    at one row). Only month/day is stored, so the picker year is decorative (current year; Feb 29 →
    next leap year) with the caption "Repeat every year — only the month and day are saved, so the
    year in the date picker is ignored." Rows missing any of the four date fields are discarded on
    save. Rule is `daysOfWeek: []` (all days), `00:00`/`00:00`, named `"Season M/D–M/D"`.
  - Existing rules hydrate into the form; Save diffs form vs server (create/update/delete) and
    removes all rules of the other mode. Caption shows `Times in {tz}.`; status refreshes every
    60 s while open.
- [x] **Client:** `client.ts` adds `LibrarySchedule` + `ScheduleStatus` types and
  `listSchedules/createSchedule/updateSchedule/deleteSchedule/getScheduleStatus/evaluateSchedule`.
- [x] **Bottom bar:** `Schedule` button placed immediately RIGHT of `Sync` (disabled when no library
  is selected; title "Choose when this library appears"), followed by a status span:
  `Stops showing {when}` (active) / `Shows next {when}` (inactive) / `Always on` (fail-open) /
  empty when there are no rules. `scheduleLabel` polls `getScheduleStatus(selectedLibraryId)`
  every 60 s.
- [x] **Verified:** `docker compose build web` green after removing an unused `LibrarySchedule`
  import (TS6133); served bundle `assets/index-CnXk84hj.js` contains the new strings. Sandbox API
  round-trip: daily Mon 18:00–22:00 → `scheduleCount 1`, `nextTransitionAt 2026-11-16T18:00Z`;
  season 12/1–1/31 → count 2; `evaluate` → empty report; delete both → `alwaysOn: true`,
  `scheduleCount: 0`. Backend re-verified 2026-11-21: `BASE=http://localhost:5001
  ./test-schedules.sh` → 27 passed / 0 failed (fail-open, day-of-week rules, overnight windows,
  year-wrapping seasons, validation rejections, evaluate report) and `docker compose build api`
  green. Redesign bundles: `assets/index-BHn5wMW3.js` on prod :5173 and sandbox :5174 (sandbox web
  reuses the `curatarr-web:latest` image).

### Short-term Filters: Rating + Studio/Network (2026-11-14)
- [x] **Filter status UI fixed (2026-11-15):** `SelectFilter` button labels no longer
  hijack to "Loading details..." — they always show the selected value (or "All X")
  + arrow; the `backfilling` flag now only drives the open-dropdown empty hint.
  New inline status text after the dropdowns: `Loading... 12,500 / 38,212` while the
  foreground backfill runs (total computed locally, completed = total − `resp.pending`
  per batch, so it climbs live), `✓ Completed` for 10s then auto-hide (timer cleanup
  on unmount/new run). 60s foreground cap → `x / y — finishing in background` for
  10s; TMDB-stuck → `x / y — no progress, will retry in background` for 10s.
  Redundant success/info toasts removed (error toast kept). No status at all when
  pending=0 (prod today).
- [x] **Schema:** 3 new `SourceItem` columns — `Rating` (US cert), `ProductionCompanies`
  (movies, `|`-separated), `Networks` (shows/anime, `|`-separated). Model properties added
  (fresh DBs via `EnsureCreated`); prod DB migrated with raw
  `ALTER TABLE SourceItems ADD COLUMN ...` while the API was stopped (no EF migration).
  Sentinel semantics: `null` = not fetched (retry), `""`/`"[]"` = fetched but none found
  (never retried)
- [x] **MetadataProvider:** `GetMovieGenreIdsAsync`/`GetShowGenreIdsAsync` → single
  `GetCatalogDetailsAsync(tmdbId, mediaType)` — one TMDB round-trip via
  `append_to_response=release_dates` (movies) / `content_ratings` (shows); extracts US
  certification, production companies, networks, genres. Throws on TMDB failure so the
  caller's per-item catch leaves columns NULL (retry) instead of marking them empty
- [x] **Backfill:** shared `DetailsBackfill` static helper (`Pending` predicate +
  `RunBatchAsync`) used by both the `/api/backfill-genres` endpoint (route unchanged, now
  returns `pending`, batch 200→50 to stay under nginx's 60s proxy timeout) and the hourly
  background service (renamed `GenreBackfillService` → `DetailsBackfillService`, batch 500,
  run-level progress guard breaks when a batch yields zero changes so TMDB-outage rows
  can't loop forever)
- [x] **API:** `/api/source-items` DTO exposes the 3 new fields; `/api/backfill-genres`
  response gains `pending`
- [x] **Frontend:** `client.ts` SourceItem + `backfillDetails` (was `backfillGenres`);
  `showGroups.ts` aggregates `rating`/`networks` (first non-empty); `PosterGrid.tsx` —
  generic `SelectFilter` dropdown component, `ratingFilter`/`studioFilter`/`networkFilter`
  states, distinct-value option memos (canonical rating order, alphabetical studios/
  networks) computed from rendered entries, chained filter predicates (studio/network
  narrow to their own media kind), `ensureDetails` on dropdown open (60s foreground cap,
  mid-run item refresh so open dropdowns grow live, toast pointing at background job),
  filters reset on `libraryMediaType` change, SignalR merged-item literal updated
- [x] **Backfill complete:** 38,212/38,212 processed, 0 failed, pending=0
  (Rating 35,396+2,816 empty; studios on all 1,827 movies; networks on all 36,385 shows)
- [x] **Verified:** API returns new fields (curl); sandbox re-seeded with post-ALTER prod
  DB + `test-api.sh` both modes (guard-only seeded PASS, full suite fresh PASS,
  `WIZARD_ONLY` fresh PASS); browser-verified via Playwright — Movie library shows
  Genres/Ratings/**Studios**, TV library shows Genres/Ratings/**Networks**; PG-13 → 409
  movies, Walt Disney Pictures → 30 movies, Nickelodeon → 11 shows, Clear filters restores;
  only console error is the expected pre-login 401
- [x] **Status UI verified (2026-11-15, Playwright on sandbox):** nulled 1,500 Rating
  rows → opening a dropdown showed `Loading... 555 / 1,655` → `755` → `1,155` →
  `1,355` → `1,555` → `1,655 / 1,655` → `✓ Completed` (≈15s total) → auto-cleared
  after 10s; selecting PG-13 mid-run kept the button label as `PG-13 ▸` (never
  "Loading details..."); prod (pending=0) shows no status text at all

### Plex OAuth + Refresh Trigger (2026-11-16)
- [x] **Model:** singleton `PlexSettings` entity in `Curatarr.Engine/Data/Models.cs`
  (`ClientId`, encrypted `Token`, `AccountName`, `ServerName`, `ServerUrl`,
  `RefreshOnChange`, `LastRefreshAt`, `LastRefreshStatus`) + `DbSet<PlexSettings>`;
  EF migration `20261116015100_AddPlexSettings` generated with `dotnet ef` and
  smoke-tested against a throwaway SQLite DB. Token is protected with
  `IDataProtector("Curatarr.Plex.Token")` and is never returned by any endpoint.
- [x] **API:** `Curatarr.Api/PlexEndpoints.cs` (all Admin-only) — `GET /api/plex/status`
  (`linked`, account/server/URL, refresh toggle, last refresh), `POST /api/plex/oauth/start`
  (creates a strong PIN via `https://plex.tv/api/v2/pins?strong=true` and returns the
  `app.plex.tv/auth#?clientID=…&code=…` URL), `GET /api/plex/oauth/poll/{pinId}`
  (exchanges the PIN for a token, then reads `/api/v2/user` + `/api/v2/resources`),
  `PUT /api/plex/settings` (server URL override + refresh toggle), `DELETE /api/plex/link`.
- [x] **Refresh service:** `Curatarr.Api/PlexService.cs` — maps the library `OutputPath`
  through the existing `PathMapping` rows (longest `FromPrefix`, `/`-normalized,
  case-insensitive), matches the Plex `/library/sections` location, then
  `POST /library/sections/{key}/refresh`. Fire-and-forget via `Task.Run` on its own DI
  scope with a cached linked check; failures are logged and recorded on `PlexSettings`
  and never bubble into reconcile.
- [x] **Hooks:** full reconcile (`POST /api/reconcile`), library output-path change,
  manual schedule evaluate (`POST .../schedules/evaluate`), and every schedule flip in
  `LibraryScheduleService`.
- [x] **Frontend:** `PlexPage.tsx` (Settings → Plex, admin-only) with Link Plex popup +
  PIN polling, linked account/server, editable server URL, "Refresh Plex after changes"
  toggle, Unlink, last-refresh line; `client.ts` `PlexStatus` + methods; `Sidebar.tsx`
  gains `settings-plex`, a gear icon on Settings and a stacked-server icon on Server;
  `App.tsx` route.
- [x] **Verified:** `docker compose build api` and `build web` green (migration generated
  in a temporary .NET 10 SDK container); prod DB backed up to
  `bak-config/promotion/20261116-015607-plex/curatarr.db`, `docker compose up -d --no-build
  api web` applied the migration on startup; `GET /api/plex/status` →
  `{"linked":false,…,"refreshOnChange":true}`; `POST /api/plex/oauth/start` returned a real
  PIN (`pinId 1184023830`) proving plex.tv is reachable from the API container; served
  bundle `assets/index-D9n9BkOo.js` contains `Link Plex`, `Refresh Plex after changes`,
  `Unlink`, `Plex account`.

### Bottom-bar library label (2026-11-16)
- [x] **`Library:` prefix:** `Curatarr.Web/src/components/BottomBar.tsx` now renders a
  `Library:` label (12px, `--text-secondary`, `whiteSpace: nowrap`) immediately before the
  library `<select>`, both wrapped in one fragment so the dropdown keeps its layout on
  desktop and mobile; the select also gained `aria-label="Library"`.
- [x] **Verified:** `docker compose build web` green, `docker compose up -d --no-build web`,
  and the served bundle `assets/index-BYqDsGgu.js` contains `Library:` (3 matches) and one
  `aria-label` — hard refresh (Ctrl+Shift+R) needed to see it.

### Uniqueness validation (2026-11-16)
- [x] **API guards:** `Curatarr.Api/Program.cs` — `POST/PUT /api/source-roots` now trim
  `Name`/`Path`, reject blank values (400), and return `409 { error }` when another root uses the
  same name (case-insensitive) or the same path; `POST/PUT /api/virtual-libraries` do the same for
  `OutputPath`. Paths are compared through a `NormalizePathKey` helper (backslashes → `/`, trailing
  separators dropped), and PUT handlers exclude the record being edited so self-updates pass.
- [x] **Messages:** "A source with that name already exists — choose a different name.",
  "A source with that path already exists.", "Another library already uses that output path."
  `client.ts::extractErrorMessage` already understands `{ error }`, so the UI shows them as-is.
- [x] **Verified (prod :5000, /tmp/ck.txt):** duplicate name `MOVIES` → 409, duplicate path
  `/media/movies/` → 409, duplicate library output path `/output/VirtualLibraries/peeles_kids_movies/`
  → 409; new source + new library → 200; self-update PUTs (source + library) → 200; test rows
  deleted afterwards. `docker compose build api` green, `up -d --no-build api` deployed.

### Plex auto-refresh fixes (2026-11-17)
- [x] **Diagnosis:** `GET /api/plex/status` showed `No matching Plex section`. Three independent
  causes: approve/unapprove never called `PlexService`, the symlink was only created by Sync so even a
  refresh had nothing new to index, and the `/output` PathMapping target did not match the path Plex
  reports for those sections.
- [x] **Mark/unmark stay batched; Sync does the work** (`Curatarr.Api/Program.cs`): approve/unapprove
  were briefly changed to reconcile + refresh Plex inline, which fired a Plex scan on every click. They
  are back to recording intent only (`PlexService`/`SymlinkEngine`/`ScheduleTimeZone` params removed
  again); `POST /api/reconcile` (the **Sync** button) runs the schedule-aware `ReconcileAsync` for all
  libraries and then `plex.TriggerInBackground(null)` once. Doc comments in both endpoints spell out the
  batch semantics.
- [x] **Unapprove prunes the link** (`Curatarr.Engine/SymlinkEngine.cs`): stale-record cleanup was scoped
  by the library's current `VirtualLibraryItem` ids, so a record whose item had just been unapproved was
  filtered out and never deleted. Scope now also matches `SymlinkRecord.VirtualRootPath` against the
  library's `OutputPath`, so the link is pruned by the next Sync.
- [x] **Path matching** (`Curatarr.Api/PlexService.cs`): `SectionContains` gained a path-boundary suffix
  branch (a Plex location like `/Volumes/Macintosh HD/Volumes/data/VirtualLibraries/x` now matches the
  translated `/Volumes/data/VirtualLibraries/x`), plus permanent diagnostic logs listing the translated
  paths and every section's locations. Data: `/output` PathMapping repointed to
  `/Volumes/Macintosh HD/Volumes/data` (id `2405c173-b5ee-4418-a9e6-c3a9e688b549`) alongside the existing
  `/media` → `/Volumes/data` row.
- [x] **Verified:** `docker compose build api` green, prod redeployed. Mark `12 Days of Christmas Eve
  (2004)` → nothing on disk, `lastRefreshAt` unchanged. Press **Sync** → symlink created under
  `/output/VirtualLibraries/peeles_kids_movies` and `lastRefreshStatus` = `Refreshed 2 section(s)`.
  Unmark + Sync → link and empty folder removed. Leftover test approvals cleaned up; library back to its
  4 real items (approved-item-ids and on-disk folders match 1:1).

### Plex client refresh parity — path-scoped scan (2026-11-17)
- [x] **Diagnosis:** the server scan worked (`Refreshed N section(s)`) but Plex **clients** only
  showed new titles after navigating. Sonarr/Radarr source (`src/NzbDrone.Core/Notifications/Plex/
  Server/PlexServerProxy.cs`, identical in Radarr) shows why: `Update()` calls
  `GET library/sections/{id}/refresh?path={absolute folder}` — a **partial scan of just the changed
  item folder**. Curatarr posted a bare `POST /library/sections/{key}/refresh`, a full-section
  rescan that queues behind whatever Plex is already scanning.
- [x] **API changes:** `PlexService.TriggerInBackground(IEnumerable<string>? libraryIds,
  IReadOnlyCollection<string>? changedPaths = null)` and matching
  `TriggerLibraryRefreshAsync(..., changedPaths)`; paths are translated through `PathMapping` and
  matched to a section by the new `ResolveSectionPath` helper (section location is a prefix of the
  changed path), then one `GET …/refresh?path=<encoded>` per (section, folder). Falls back to the
  previous full-section refresh when no section covers the changed folder. Status is recorded as
  `Refreshed N path(s)`; logs add `(path-scoped) for N changed folder(s)`.
- [x] **`POST /api/reconcile`:** derives the changed item folders from
  `ReconcileReport.Created`/`Removed` via `Path.GetDirectoryName`, de-duplicates them, and passes
  them to `plex.TriggerInBackground(null, changedFolders)`. Schedule/output-path triggers keep
  using the full-section fallback.
- [x] **Admin diagnostics added:** `GET /api/plex/sections` (key/title/type/locations) and
  `GET /api/plex/sections/{key}/recent?limit=N` (newest items by `addedAt`, with the exact file
  path Plex stored) in `Curatarr.Api/PlexEndpoints.cs`, both Admin-only. Used to confirm Garfield
  was scanned into section 13 (`addedAt 2026-11-17T02:44:06`, 2 s after Sync).
- [x] **Removals force a full section scan (follow-up):** a path-scoped scan can't express a
  deletion (Plex ignores a `path` that no longer exists), which is why removing Aladdin lingered
  until a manual client refresh. `TriggerInBackground`/`TriggerLibraryRefreshAsync` now take
  `changedPaths` **and** `removedPaths`; added folders get `GET …/refresh?path=…&force=1`, removed
  folders force `GET …/refresh?force=1` on every section that covered them. New helpers
  `RefreshPathAsync`, `RefreshSectionAsync`, `SendRefreshAsync`.
- [x] **Apple TV client follow-up:** the desktop Plex app updated instantly while tvOS still
  lagged. Checked all of Sonarr/Radarr's Plex code — they add nothing beyond the same scan call
  (only a `plex.tv` ping, request debouncing, and client-identifier params). The real cause was a
  server setting: `FSEventLibraryUpdatesEnabled` (Scan my library automatically) was **off**, so
  Plex only ever noticed changes when an external tool nudged it, and tvOS caches Home hubs hardest.
  New Admin endpoints `GET /api/plex/prefs` (selected prefs) and `PUT /api/plex/prefs`
  (`{"FSEventLibraryUpdatesEnabled":"1"}`) in `PlexEndpoints.cs`; auto-scan enabled on the server
  and verified (`True`). Hardlinks are not an option here: `/media` and `/output` are separate bind
  mounts, so `ln` fails with `Invalid cross-device link`.
- [x] **Verified (prod :5000):** Garfield — Sync → `Refreshed 1 path(s), 0 section(s) … 1 added / 0
  removed`, in Plex section 13 with `addedAt` 2 s later; unapprove + Sync → `Refreshed 0 path(s), 1
  section(s) … 0 added / 1 removed`, dropped from Plex; re-approve + Sync → back with a fresh
  `addedAt`. `docker compose build api` green, `up -d --no-build api` deployed.
- [x] **Verified (prod :5000):** approve `12 Days of Christmas Eve (2004)` + Sync → log
  `Plex refresh: Refreshed 1 path(s) (path-scoped) for 1 changed folder(s).`; unapprove + Sync → same
  with the folder pruned. `docker compose build api` green, deployed via
  `up -d --no-build api`.

### Library grid performance — lazy cards + smaller posters (2026-11-17)
- [x] **Symptom:** library page slow to load; Safari eventually reloads it with a memory-usage
  warning. Cause: all ~82k source items held in state and one full card + raw `w500` `<img>` per
  filter result in the DOM.
- [x] **`PosterGrid.tsx`:** new `LazyMount` wrapper (hand-written `IntersectionObserver`,
  `rootMargin: "1200px 0px"`) mounts a card only while it is near the viewport and unmounts it once
  it scrolls away, with an `aspect-ratio: 2/3` skeleton so the grid height is stable. Both
  `PosterCard` and `ShowCard` render inside it.
- [x] **Posters:** new `posterThumb()` rewrites TMDB `/t/p/w500/` → `/t/p/w185/` for grid cards
  (details modal still uses the original URL); both `<img>`s gained `loading="lazy"`,
  `decoding="async"`, `draggable={false}`.
- [x] **Verified** (prod web bundle `assets/index-Dj-7WC9I.js` then `index-CkYrhUBO.js`, Playwright on
  :5173): of 3,987 grid cards only 3 mounted at rest; after scrolling the MAIN container 19,283px
  only 6 posters were mounted; DOM divs ~16k vs ~35k+ with every card rendered.
- [ ] **Follow-up:** server-side paging/windowed fetching (the full catalog is still downloaded into
  memory), optional `content-visibility: auto`, Safari soak test.

### View filter + card icon states — batch 1 (2026-11-17)
- [x] **Sprint 4 #1 / Sprint 5 #6 shipped together.** `client.ts` now annotates `SourceItem` with
  `approved`/`approvedAt`/`hidden` from `/api/source-items?libraryId=…`, `VirtualLibrary` with
  `lastReconciledAt`, and exposes `listHiddenItemIds`/`hideItems`/`unhideItems`. `App.tsx` passes
  `lastReconciledAt` to the grid.
- [x] **`PosterGrid.tsx`:** first filter `All/Library/Unmarked/Hidden <Type>` before Genres
  (default All; hidden excluded except in Hidden view; per-library reset), plus a pinned
  `CardActions` row — gray→yellow→green check for Mark (yellow until `lastReconciledAt >
  approvedAt`), gray/yellow eyeball for Hide (persisted immediately), red circle-slash overlay in
  the Hidden view. All icon buttons keep `title` + `aria-label`; hide uses optimistic update with
  rollback + toast. Live-scan merges and post-backfill reloads preserve the per-library flags.
- [x] **Sandbox-first rollout:** `promote.sh --sandbox-only` green (build + smoke + guard-only
  `test-api.sh`), browser-verified on :5174 (four filter options, Hide → 200 and item leaves All
  view, Hidden view shows 3 red-circle-slash eyes and unhide toggle), sandbox hidden test data
  deleted, then `promote.sh --prod-only` → `prod smoke: OK`. Final bundle
  `assets/index-QTnbk16h.js`. Spec: `.project_documents/RESEARCH-batch1-filter-and-icons.md`.

### Server-side paging & filtering — S5 #8 (2026-11-17)
- [x] **Backend:** new `Curatarr.Api/LibraryGridEndpoints.cs` (`GET /api/library-entries`,
      `GET /api/library-facets`) backed by pure `Curatarr.Engine/LibraryGrid/` helpers (filter
      predicates, exact `showGroups.ts` port, limit clamp 1..200 default 60, stable sort/paging,
      facets). `/api/source-items` untouched. Registered in `Program.cs` next to `MapPlexEndpoints`.
- [x] **Frontend:** `client.ts` types + methods; `PosterGrid.tsx` fetches 60-entry pages with a
      250 ms search debounce, page-0 refetch on filter change, IntersectionObserver append with
      id/key dedupe; LazyMount/posterThumb, letter rail, view filter and card icons all preserved.
      `VITE_LIBRARY_PAGING` (default on, `=false` restores legacy path).
- [x] **Tests / builds:** new `Curatarr.Tests` xUnit project — 44/44 passing via a .NET 10 SDK
      container; `docker compose build api` and `docker compose build web --no-cache` green.
- [x] **Verified (sandbox :5174):** initial entries payload 77 KB / 60 items (`1877 total`) and
      facets 49 KB; 3 posters and 3 icon rows mounted at rest; scrolling `main` to 9000 appended
      pages (3 → 6 mounted, total unchanged); Library Movies and Hidden Movies views correct; no
      console errors. Sandbox-first promotion via `scripts/promote.sh --sandbox-only` then
      `--prod-only`; prod smoke OK. Hard refresh required for the new bundle.

### Automatic approve/hide rules — Sprint 6 (2026-11-17)
- [x] **Backend:** migration `AddLibraryRules` adds `LibraryRules`, `LibraryRuleRuns` and a
  `SetByRule` marker on `VirtualLibraryItems`/`HiddenItems`; pure `Curatarr.Engine/Rules/`
  helpers (`RuleFields`, `RuleEvaluator`, `RuleApplication`) reuse `GridFilters` so rule values
  match grid filters. New `Curatarr.Api/LibraryRuleEndpoints.cs` exposes `GET /api/library-fields`,
  `GET/POST/PUT/DELETE /api/virtual-libraries/{id}/rules`, `POST .../rules/preview` (read-only)
  and `POST .../rules/apply` (audited), gated like the existing hidden-items endpoints.
- [x] **Semantics:** generic field/operator/value rules for genre, director/creator, cast, studio,
  network, rating, year and media type; manual marks/hides always win; last matching enabled rule
  wins; hide removes rule-owned approvals and approve removes rule-owned hides; physical removal
  waits for Sync; re-apply is idempotent and records a run.
- [x] **Frontend:** new Rules page/sidebar entry and per-library `LibraryRulesPanel` with builder,
  Preview and Apply confirmation warning that removals need a Sync.
- [x] **Verified:** 54/54 xUnit tests green in the .NET 10 SDK container; both docker builds green
  (`index-TFBTsAsG.js`); sandbox-first rollout — rule create 200, preview `{toApprove:92}`,
  Rules page + Apply dialog verified, test rule deleted, `promote.sh --prod-only` → prod smoke OK.

### Hide icon parity, Library/Hidden exclusivity and pinned actions — S5 #9 (2026-11-17)
- [x] **`hiddenAt` plumbing:** `/api/source-items?libraryId=` and `/api/library-entries` now expose
  `HiddenAt` from `HiddenItem.CreatedAt` (show = newest stamp across episode ids); `SourceItem` and
  `LibraryEntry` client types updated.
- [x] **Icon parity:** the Hide eye and the Mark check share the same `reconcileStatus` boundary —
  no stamp/no reconcile = yellow (pending), `lastReconciledAt > at` = green (synced); the eye stays
  gray when visible and keeps the red circle-slash overlay in the Hidden view. Tooltips/aria-labels
  state the current state and next action.
- [x] **Library ↔ Hidden:** clicking Hide on a marked card or Mark on a hidden card confirms that
  the other state is removed and that a Sync is still required; on accept both changes are applied
  sequentially. Optimistic `hiddenAt` patches roll back on failure.
- [x] **Pinned row:** shared `CARD_ACTION_ROW_STYLE` (30px, `marginTop:auto`) used by movie/show
  cards and both skeletons so the action row sits at the same spot on every poster.
- [x] **Verified:** 59/59 tests green, both docker builds green (`index-XzBcLR7h.js`); sandbox
  verified hiddenAt API, yellow eye + red slash and confirm wording, hidden test data cleaned,
  `promote.sh --prod-only` → prod smoke OK.

### Hide/unhide + view-filter blank grid — Bug #3 (2026-11-17)
- [x] **Root cause:** opening any filter dropdown called `ensureDetails()`; in paged mode
  `ensureDetailsPaged()` unconditionally refetched page 0 with the active view. After an optimistic
  unhide in the Hidden view that replaced the page with an empty result, and the zero-result early
  return sat above the toolbar JSX, unmounting the filter bar so the user could not switch back to
  All.
- [x] **Fix (`PosterGrid.tsx`):** only refetch page 0 when the detail backfill actually changed
  rows — with no backfill work, just reload facets and keep the current optimistic page. Moved the
  zero-result message into the grid below the toolbar and derived it from the visible groups, so
  the filter bar is always available and any empty filter combination is recoverable.
- [x] **Verified:** sandbox repro on `assets/index-BfJ29BrT.js`: Hide → Hidden Movies → unhide →
  reopen dropdown kept the filter bar and `All Movies` option; choosing All restored 1,877 titles
  and 3 visible cards. No console errors; sandbox hidden list re-read as `[]`; `docker compose build
  web` green; `promote.sh --sandbox-only` passed guard tests; `promote.sh --prod-only` → prod smoke
  OK.

### Hide/unhide batched with Sync — Bug #4 (2026-11-17)
- [x] **Root cause:** hide/unhide were applied immediately — `HiddenItem` was inserted/deleted at
  click time, both grid endpoints filtered by it at once, and `SymlinkEngine` never read it, so
  Sync did not remove a hidden title's symlink.
- [x] **Two-phase model:** added `HiddenItem.UnhideRequestedAt` (migration
  `20261117191901_AddHiddenItemUnhideRequests`) and pure `HiddenItemState` helpers. Hide request
  stamps `CreatedAt`; unhide request stamps `UnhideRequestedAt`. Effective hidden state is derived
  against the library's `LastReconciledAt`, so pending hides stay visible with a yellow eye and
  pending unhides stay in Hidden until Sync. Reconcile skips hidden approvals (removing their links)
  and clears completed unhide requests in the same pass.
- [x] **API/UI wiring:** `/api/source-items?libraryId=` and `/api/library-entries` now emit effective
  `hidden` plus the active request stamp; `client.ts`, `entryToSourceItem`, `hiddenStateFor`,
  `CardActions` and `changeHidden` keep pending state local and optimistic with rollback. Eye
  labels now say Hide/Unhide requested — press Sync to apply/restore.
- [x] **Verified:** 64/64 tests green; both Docker builds green. Sandbox full cycle: Hide → yellow
  eye, card stays, total 1,877 → Sync → green eye, card leaves All, total 1,876 → unhide → yellow
  eye, card stays in Hidden → Sync → hidden-items `[]`, total 1,877, gray eyes; 0 console errors.
  Fixed a sandbox-only `/output` ownership issue (uid 1654) before reconcile worked. Promoted with
  `promote.sh --prod-only`; prod serves `assets/index-C6WaqyNp.js`.

### Activity + Logs — Sprint 5 #1 + #2 (2026-11-17)
- [x] **Activity backend:** new `ActivityEvent` entity + EF migration `20261117221445_AddActivityEvents`;
      bounded `ActivityRecorder` channel (5,000) feeding `ActivityIngestService` (batch save + SignalR
      `ActivityCreated` broadcast). Admin-only `GET /api/activity` (cursor paging, newest first) and
      `GET /api/activity/scan-status`. Instrumented 30+ hook points across login, user/path/schedule/
      source CRUD, Plex link/unlink, scan start/finish/pause/resume/stop and Sync — tokens, passwords
      and invite/reset links are redacted via `ActivityRedaction` before persistence. No FK to
      library/user, so audit rows survive deletes.
- [x] **Logs backend:** Serilog console + rolling file sink (`{ContentRoot}/logs/curatarr-.log`, Day
      roll, 10 MB, retained-file limit) + 2,000-entry in-memory ring. `logs-config.json` in the
      content root persists level/retention/fileSize/archive defaults (Information / 7 days / 10 MB /
      true); `PUT /api/logs/level` switches the running level instantly. Admin-only
      `GET /api/logs`, `GET /api/logs/config`, `PUT /api/logs/config`; live streaming via `/hubs/logs`.
      `LogArchiveService` zips closed daily logs and enforces retention (7/14/21/30/60/90).
- [x] **Frontend:** Settings → Activity and → Logs are live Admin-only pages (`ActivityPage.tsx`,
      `LogsPage.tsx`), no longer "coming soon". Activity has category filters, a scan countdown card
      (elapsed/completed/total/ETA), newest-first list and Load more. Logs has Refresh, Monitor live,
      level + retention pickers, Save status and a persistent Debug warning.
- [x] **Bug fixed during verification:** `GET /api/activity` 500 on SQLite (`DateTimeOffset` cannot be
      used in ORDER BY, and `UtcTicks` navigation wouldn't translate). Now loads rows with no DB-side
      ordering and lets the pure `FilterAndPage` sort newest-first in memory.
- [x] **Verified:** 129/129 tests green; both Docker builds green; `promote.sh --sandbox-only` passed.
      Sandbox Playwright: `/api/activity` 200 with newest-first persisted rows + nextCursor; scan
      pause/resume produced live `ScanPaused`/`ScanResumed` events; Logs streamed real entries, Debug
      warning shown and level restored, config unchanged; 0 page errors. Promoted with
      `promote.sh --prod-only`; prod serves `assets/index-BoiMd5Cp.js` (hard refresh required).

### Grid refresh after Sync — Bug #5 (2026-11-17)
- [x] **Symptom:** after hiding a title (yellow, card stays) and clicking **Sync**, the card stayed in
      `All <Type>` until the view filter was toggled to Hidden and back. Follow-up regression to
      Bug #3's removal of the unconditional refetch.
- [x] **Root cause:** `handleReconcile()` in `App.tsx` never bumped `posterRefreshKey`, so
      `PosterGrid` never refetched page 0 / annotations after Sync. The optimistic `hidden=false`
      from the pending hide persisted locally until a filter toggle changed the fetch callback
      identity. Backend was already correct (effective hidden derived from the server's own
      `LastReconciledAt`).
- [x] **Fix:** `handleReconcile()` now calls `setPosterRefreshKey(k => k + 1)` (forces page-0 refetch)
      and `setLibrariesVersion(v => v + 1)` (reloads libraries so `lastReconciledAt` updates and the
      eye renders green). Frontend-only change.
- [x] **Verified:** sandbox by clicking the UI **Sync** button with no reload/filter toggle — the
      yellow "Hide requested" card vanished immediately, total 1,877→1,876; Hidden view eye green
      (`var(--color-success)`); unhide → Sync restored total to 1,877 and cleared hidden-items `[]`;
      0 console errors. `docker compose build web` green; `promote.sh --sandbox-only` guard tests
      passed; promoted with `promote.sh --prod-only`; prod serves `assets/index-Bo0SBBNN.js`.

### Oversized posters after fast scroll — Bug #2 (2026-11-18)
- [x] **Confirmed on mobile:** 3 posters/row; after a fast scroll to *A Beautiful Mind* the
  rightmost poster *A Better Tomorrow* rendered oversized. Desktop repro attempts had failed
  (57,602 rAF samples, max = column width; a 316px reading was the grid container, not a card).
- [x] **Root cause:** mobile grid used `repeat(3, 1fr)`; `1fr` = `minmax(auto, 1fr)`, so a loaded
  poster's intrinsic min-content width could blow out the rightmost track as pages appended.
- [x] **Fix (`PosterGrid.tsx`):** mobile grid → `repeat(3, minmax(0, 1fr))`; `LazyMount` wrapper and
  PosterCard/ShowCard roots got `minWidth: 0, width: "100%"` (`overflow: hidden` on the wrapper) so
  skeleton and mounted cards reserve the same width.
- [x] **Verified:** `docker compose build web` green; `promote.sh --sandbox-only` passed; mobile
  Playwright at 390x844 with injected posters across two rapid multi-page flicks (34→94→132 cards)
  kept all three tracks ~94.7px and every card 94.7px, no oversize, no horizontal overflow, 0 page
  errors. Promoted with `promote.sh --prod-only`; prod bundle `assets/index-B34SAEQX.js`.

### Wizard media-type once + media-type-scoped scan — Sprint 4 #1 + Sprint 5 #3 (2026-11-18)
- [x] **Wizard:** first-boot wizard now asks for one media type on the Account step and no longer
  shows per-source or per-library selects. `mediaType` is the single top-level bootstrap field;
  `BootstrapWizardEndpoints.TryResolveMediaType` parses it once (legacy `LibraryMediaType` still
  accepted), returns 400 for invalid values before creating any user, and applies the resolved kind
  to every source root and the first library. `test-api.sh` payloads updated to the new shape.
- [x] **Scoped scan:** `POST /api/scan` accepts optional `{ libraryId }`; a supplied id resolves the
  library's `MediaKind` (404 when missing) and `ScanScope.FilterRoots` keeps only matching roots;
  no id keeps the legacy all-roots behavior. Web passes `selectedLibraryId` from `App.handleScan`
  through `client.ts scan(libraryId?)`; the BottomBar Scan button is disabled without a library.
  Scan activity details now name the scope.
- [x] **Tests:** `TryResolveMediaType` and `ScanScope.FilterRoots` extracted as pure helpers and
  covered by `Curatarr.Tests/BootstrapAndScanScopeTests.cs` (6 facts) — valid new shape, legacy
  fallback, invalid/missing values, exact Anime match and all-roots fallback. Full suite **137/137
  passed**; `docker compose build api web` green.
- [x] **Verified:** sandbox-only promotion passed; fresh-DB wizard happy path + one-shot guard
  passed with the new payload (`BASE=:5099 WIZARD_ONLY=1 ./test-api.sh`), invalid `mediaType`
  returned 400 with no bootstrap. Scoped scans in sandbox returned 1,877 (Movie library), 36,498
  (Show library) and 38,375 (no body / all roots) items with matching `ScanStarted` activity rows.
  Promoted with `promote.sh --prod-only`; prod serves `assets/index-DnVNfPJu.js`.

### Multiselect, match tooling + admin roles, invite accept — Sprint 5 #4/#5 + Bug #1 (2026-11-18)
- [x] **Bug #1 invite accept:** `POST /api/auth/accept-invite` now returns `{message,email}`;
  `AcceptInvitePage.tsx` shows a success toast containing the email, attempts auto-login, and
  navigates to `/` with `replace:true` (signed-out login screen on failed auto-login). Added a
  test-only, capped invite-token sink (`InviteTokenSink.cs`, Admin-only `GET /api/test/invites`,
  enabled only by `Testing__InviteSink=1` in the sandbox compose), captured on invite/resend, with
  opt-in `INVITE_E2E=1` coverage in `test-api.sh` (no docker-log scraping).
- [x] **Bulk multiselect (`PosterGrid.tsx`):** Multiselect toggle in collapsed/expanded toolbars,
  top-right checkbox overlay on cards, Select all/Clear, and a bulk bar with Mark/Hide selects.
  Apply is enabled only with ≥1 selected AND ≥1 non-No-Change action; uses existing single-id APIs
  (representative id for shows' Mark, all episode ids for Hide), refreshes page 0/facets/approved
  ids once, shows one summary toast, and confirms only Mark+Hide / Unmark+Unhide combinations.
  Selection is cleared on library/media-type/view/filter changes and page-0 refetch.
- [x] **Match tooling:** shared `TitleQueryParser` fixes dotted filenames (`Groundhog.Day.1993.
  2160p.UHD.BluRay.X265-IAMABLE` → `("Groundhog Day", 1993)`) for scan lookups and the new admin
  Match modal (prefill, auto-search, manual fallback). Added `TitleSearchResult`/`SearchTitlesAsync`
  (movie + TV, up to 10 results), `SourceItem.ManuallyMatched` + EF migration, scan merge that freezes
  metadata for manual matches (non-metadata fields still update), `unmatched=true` on grid/facets,
  admin-only `GET /api/metadata/search` + `POST /api/metadata/match`, and admin-only Unmatched toggle.
  Match clears only backfill fields (GenreIds/Rating/ProductionCompanies/Networks/Director/Creators/
  Cast), keeping applied TmdbId/PosterUrl/Overview/title/year; logs `ActivityCategory.Source`.
- [x] **Admin promotion:** `GET /api/users` includes roles; guarded `PUT /api/users/{id}/roles`
  (`{roles:string[]}`) blocks Viewer access, self-demotion (including the original admin) and the
  last Admin. `UsersPage.tsx` has a Role column with self-demotion disabled; `TECHNICAL.md` updated.
  Opt-in `ROLE_E2E=1` shell coverage exercises 403/400 guards, promotion/demotion and restore.
- [x] **Verification:** `promote.sh --sandbox-only` PASSED (build + sandbox smoke + guard-only
  `WIZARD_ONLY=1`); fresh-DB `INVITE_E2E=1 ROLE_E2E=1 BASE=http://localhost:5002 ./test-api.sh`
  PASSED all sections 0-10 on a temporary isolated API container (seeded sandbox untouched).
  A first ROLE_E2E failure was a TEST bug (original admin demoted itself with its own cookie,
  correctly rejected); fixed by using the promoted admin's cookie and a fresh restore cookie.
  Playwright on sandbox :5174: served bundle `assets/index-sYy6R4Bq.js` / `assets/index-CoR6XILo.css`
  (was `assets/index-DnVNfPJu.js`), 0 console errors; multiselect gating/Cancel, Unmatched empty
  state (`No unmatched titles. Titles without a metadata match will appear here.`), Users Role
  column, and invite accept in an isolated context (auto-login as `invite-ui@example.com` + success
  toast) all verified; disposable invite user deleted. No live Match modal test — the seeded sandbox
  had no unmatched titles. Promoted with `promote.sh --prod-only` → `prod smoke: OK`; prod serves the
  same `index-sYy6R4Bq.js` / `index-CoR6XILo.css` bundle (hard refresh required).

### PosterGrid toolbar/Unmatched UI refinement (2026-11-18)
- [x] **Unmatched as a view:** `PosterGrid.tsx` now makes `unmatched` a `ViewFilter`; the
  `Unmatched Movies` / `Unmatched TV Shows` / `Unmatched Anime` / `Unmatched Items` choice is the
  last option in the existing All Movies/TV Shows dropdown (admin-only) instead of a separate
  toolbar button. `pageParams`, `loadFacets`, active-filter counts and show-card Match visibility
  now use `viewFilter === "unmatched"`; the old `unmatchedOnly` state was removed.
- [x] **Toolbar/mobile layout:** multiselect is now an icon-only button at the end of the
  expanded filter row (inline SVG of stacked selectable squares, styled like the Noun Project
  "Multiple Select" line icon rather than importing the asset). The collapsed row shows only
  `Show` (+ active count) plus details status; `Hide filters` is now `Hide`, and hiding filters
  also exits multiselect.
- [x] **Checkbox visibility:** card checkbox overlays are rendered only while multiselect is
  active; poster tap toggles selection in that mode, otherwise it opens details.
- [x] **Verification:** `npx tsc --noEmit` clean; `promote.sh --sandbox-only` PASSED
  (2026-11-18T04:42:28Z), new bundle `assets/index-CFYEqZWo.js` / unchanged
  `assets/index-CoR6XILo.css`; `sandbox smoke: OK`; guard-only `WIZARD_ONLY=1` passed.
  Playwright at mobile width on sandbox :5174 confirmed: Unmatched Movies and Unmatched TV Shows
  are the last dropdown options, the icon-only multiselect control sits after `Hide`, the collapsed
  toolbar shows only `Show`, no `Select title` checkboxes exist until multiselect is tapped,
  tapping a checkbox and tapping another poster each increment the selection count, `Cancel`
  removes checkboxes, and browser console errors remained 0. `promote.sh --prod-only` PASSED
  (2026-11-18T04:44:38Z) with `prod smoke: OK`; prod serves `assets/index-CFYEqZWo.js` /
  `assets/index-CoR6XILo.css`. iOS Safari needs a hard refresh/cache-bust to pick up the bundle.

### Match on movies/TV/anime via icon (2026-11-18)
- [x] **Match control:** movie cards now get the same Match affordance as show/anime cards; the
  literal visible word `Match` was replaced everywhere by a 22x22 magnifying-glass icon with
  `aria-label="Match"` / `title="Match"`. A shared `MatchIconButton` shifts below the selection
  checkbox when multiselect is active so the two controls never overlap. Anime is covered:
  `Unmatched Anime` is the last view option for anime libraries, anime groups use `ShowCard`, and
  movie-kind anime rows use `PosterCard`; the API maps Anime metadata searches to TV search.
- [x] **Match flow generalization:** `MatchModal` now takes a `MatchTarget` union (movie `SourceItem`
  or show/anime `ShowGroup`), uses `sourceItemId` for single movies and `representativeId` for shows,
  and keeps the correct `mediaType` for search/apply. The description/toast wording is title-generic.
- [x] **Prefill bug found and fixed:** the 1080p Charlie Brown card initially prefilled from the full
  relative path and produced `A Charlie Brown Thanksgiving x264-HANDJOB/... mkv`. The modal now
  prefers the catalog title, and client `normalizeTitleQuery` strips path separators and file
  extensions. Verified: `A.Charlie.Brown.Thanksgiving.1973.1080p.BluRay.x264-HANDJOB` prefills
  `A Charlie Brown Thanksgiving` / `1973`.
- [x] **Verification:** TypeScript clean. Sandbox builds progressed
  `index-CFYEqZWo.js` → `index-IBZk3Qd9.js` → `index-DYcHF8AE.js` (CSS unchanged
  `index-CoR6XILo.css`); final `promote.sh --sandbox-only` PASSED 2026-11-18T04:58:00Z with
  `sandbox smoke: OK` and guard-only `WIZARD_ONLY=1` passing. Playwright on sandbox :5174 as admin
  confirmed: Unmatched Movies/TV Shows/Anime are the last dropdown options; movie, TV, and anime
  cards expose only the icon with `Match` tooltip and no visible word; clicking the movie icon
  opened the corrected prefill, found `A Charlie Brown Thanksgiving` (1973 · Movie), and applying it
  removed the card from Unmatched with the success toast (sandbox-only mutation). TV show Match
  opened its modal without mutation; checkbox-vs-icon boxes did not overlap; console errors 0. A
  temporary Anime library (`TMP Anime UI Verify`) was created and deleted on the sandbox to verify
  the live Anime dropdown; the seeded sandbox was restored to its two original libraries.
  `promote.sh --prod-only` PASSED 2026-11-18T05:02:19Z (`prod smoke: OK`); prod serves the same
  `index-DYcHF8AE.js` / `index-CoR6XILo.css` bundle. iOS Safari needs a hard refresh/cache-bust.

## 🔄 In Progress

### Current State
- **Deployed to production 2026-11-14.** `curatarr-api:folder-level-symlinks` and
  `curatarr-web:folder-level-symlinks` are live; API reports healthy, web serves the
  `index-D5tOK1C1.js` bundle, Tailscale Funnel active at the public URL.
- **API healthcheck fixed** (`docker-compose.yml`): the `wget --spider` check sent HEAD
  against a GET-only endpoint → `405` → container misreported unhealthy. Now `curl -fs`
  (GET). See `DOCKER_FIXES.md` (healthcheck-HEAD-vs-GET).
- UI/browser click-through still pending with Michael at the deployed URL.

## 📋 Next Steps

### Immediate
- [x] Obtain explicit approval for production deployment
- [x] Run Step 0 backup/restore gate under `bak-config/promotion/20261114-051028-folder-level-symlinks/`
- [x] Deploy new `api` + `web` images (rollback tags `pre-folder-symlinks` retained)
- [x] Verify at `https://curatarr.tail0307d.ts.net/` (HTTP 200, API proxy 200, API healthy)
- [ ] Michael click-through: one card per show, Mark/Unmark, detail modal,
  movies unchanged, folder symlinks created on disk after approving a show

### Pending
- [ ] Add isBootstrapped/bootstrap tests to test-api.sh
- [ ] Test member invite flow
- [ ] Test scan pipeline UI
- [ ] Verify tailscale serve configuration

## 🐛 Known Issues

### Resolved
- nginx.conf was a directory instead of file - FIXED (using conf.d volume)
- Container startup order issues - FIXED with healthchecks
- Volume mount path mismatches - FIXED
- Frontend dist not accessible - FIXED (using Docker volumes)
- Docker bind mount cache issues - FIXED (using Docker volumes)
- 12 cards for a 12-episode show - FIXED (one poster per show grouping)
- Re-invite of pending user returned 400 (orphaned Identity row) - FIXED 2026-11-14 (resent path)

### Outstanding
- (none blocking — API healthcheck fixed 2026-11-14; `ts-curatarr` running 26h+)
