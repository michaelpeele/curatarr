# Remaining work — grouped analysis (2026-11-18, refs corrected 2026-11-18)

Source of truth: `.project_documents/ROADMAP.md`, unchecked items only. Completed items are excluded. Where the roadmap has duplicate or stale wording, the note below says how it was treated.

Order follows `/home/michaelpeele/ai-projects/workflow.md` where a mandated batch exists, then dependency/area grouping.

## Closed duplicates / stale references

- `ROADMAP.md:188` **Activity / logs view** — checked off `[x]` as a duplicate of Sprint 5 #1 (Activity) and #2 (Logs), both done and promoted 2026-11-18. Treated as completed; do not schedule.
- `ROADMAP.md:181` **Calendar view** — checked off `[x]` as a duplicate of Sprint 4 #2 "Calendar from metadata" (`ROADMAP.md:373`), both slices done and promoted 2026-11-18. The pre-implementation spec is retained for reference only.
- `ROADMAP.md:184` **TMDB metadata enrichment polish** was closed in docs on 2026-11-19. Posters, genres, ratings, studios/networks, people fields, and raw TMDB staging are already shipped; the only missing piece was backdrops, and the user confirmed those are not needed.
- `ROADMAP.md:176` **Docker image & container cleanup / consolidation** is partly done for sandbox (`curatarr-api:latest` / `curatarr-web:latest`), but prod still uses `build:` in `docker-compose.yml`. Treat the explicit-tag / single-container cleanup as remaining (Group D).
- `ROADMAP.md:162` **Show-level metadata** was closed in docs on 2026-11-19. Calendar slice 1 already added the explicit `SourceItem` date columns backfilled from cached `TmdbRawDetails.Json`; the next-episode goal is delivered, and episode drill-down / per-show endpoint / last-updated field were retired. See `.project_documents/PLAN-metadata-show-enrichment.md`.

The two earlier short-term duplicates and the two Group C items are now checked off; the ROADMAP has 16 genuinely open items after adding Bug #6 for the Multiselect bulk bar.

Open UI follow-up reported 2026-11-19: `ROADMAP.md` Bugs #6 — the Multiselect bulk bar must stay frozen/sticky after **Select all**; currently the options disappear after the first scroll. Track this separately from Group D; the registry/multi-arch CI work must not fold it in.

## Recommended groups / order

### Group A — Calendar + date metadata (mandated batch #6; DONE)

Items:
- `ROADMAP.md:373` Sprint 4 #2 — **Calendar from metadata** (duplicate short-term placeholder at `:181`).
- `ROADMAP.md:162` **Show-level metadata** — closed 2026-11-19; Calendar slice 1 delivered the needed air-date data and the remainder was retired (no per-show endpoint, last-updated field, or episode drill-down).

Why grouped: Calendar needs a reliable date source; `SourceItem` currently has `Year` but no release/air date fields (`Curatarr.Engine/Data/Models.cs:39-102`). The UI placeholder existed in `App.tsx`, and the per-library Mark/Hide behaviour it reuses is complete.

Confirmed decisions (2026-11-18; full detail in `.project_documents/PLAN-calendar-from-metadata.md`):
- Date source: explicit `SourceItem` date columns backfilled from cached `TmdbRawDetails.Json`; no live TMDB calls from the Calendar path.
- TV dates: `next_episode_to_air.air_date`; `episode_number == 1` gets a `New Season (#)` badge. No past episodes/seasons as separate entries.
- Access: all authenticated users (no `adminOnly` in `Sidebar.tsx`).
- Timezone: server timezone from `Schedules:TimeZone` determines today/default month and is returned by `GET /api/calendar/events` as `timeZone` / `today`; event dates stay date-only. `/api/schedules/settings` is admin-only, so Calendar never calls it.
- Link/Hide modal: **built in slice 2** (2026-11-18); reuses the library-page APIs, labels, and Sync warning.
- Calendar uses the same global toolbar and Scan/Sync behaviour as the library page via the existing shared `BottomBar` in `App.tsx`; no separate toolbar.

Slices:
1. **Read-only calendar foundation:** date columns + EF migration; pure raw-JSON date parser and backfill (including already-cached rows); `GET /api/calendar/events` with library/mediaType filters and existing membership rules; `client.ts` methods; `CalendarPage.tsx` month grid with ‹/› arrows, clickable `Month YYYY` date-picker heading, poster + title cells, `New Season (#)` badge, and library/Movie/Show/Anime filters; removed `comingSoon` from `Sidebar.tsx` and rendered `CalendarPage` from `App.tsx`. Done and promoted 2026-11-18 15:09Z.
2. **All-view link/Hide actions:** All-view event cards open a `CalendarItemModal` with library picker (`Choose a library…`), `api.approve` / `api.hideItems` (+ unapprove/unhide), exact PosterGrid wording and Sync note, using `sourceItemIds` / `representativeSourceItemId` from the Calendar endpoint. No new hidden/approved state model. Concrete-library cards stay read-only. Done and promoted 2026-11-18 18:00Z.

Slice 1 acceptance criteria: current server-zone month by default; month arrows and date-picker jump work; library and MediaKind filters work; movie releases and next-episode air dates appear on the correct day with poster/title and New Season badge; shared BottomBar Scan/Sync still works; all authenticated users can open the page; no link/hide controls yet.

Slice 2 acceptance criteria: clicking an All-view entry opens a modal that can Mark/Hide through existing APIs with the usual Sync note; full sandbox verification and promotion per workflow. Both met.

### Group B — Sprint 6 library-grid UI polish — DONE (2026-11-19)

Items:
- `ROADMAP.md:564` **UI — Loading state with animation instead of blank cards**.
- `ROADMAP.md:607` **Show quality variants in text label when multiple copies exist**.
- `ROADMAP.md:176` **Mobile & Tablet Responsive Views**.

Why grouped: all affect `PosterGrid.tsx`, card components, and the same loading/paging UI surface. Quality variants also need a shared quality parser derived from filenames/paths.

Suggested order: loading animation → quality variants → responsive sweep.

Analysis doc: `.project_documents/PLAN-library-grid-ui-polish.md` (status: DONE). Implemented, sandbox-verified, manually verified, and promoted to prod with `index-R4dyCEyO.js` / `index-fENfh4zq.css`; the three ROADMAP items were marked done only after promotion.

### Group C — Metadata/show data enrichment — RETIRED (2026-11-19)

Items:
- `ROADMAP.md:184` **TMDB metadata enrichment polish** — closed; most enrichment is already shipped. Backdrops are the large banner images and the user confirmed they are not needed.
- `ROADMAP.md:162` **Show-level metadata** — remainder closed; Calendar slice 1 already added the air-date data needed for next-episode display. Episode drill-down was retired earlier; no per-show endpoint or last-updated field is needed.

Why retired: the user's stated goal — bubble up when the next episode is on in the Calendar — is already delivered. No code was changed.

Analysis doc: `.project_documents/PLAN-metadata-show-enrichment.md` (status **APPROVED / CLOSED**). Next natural group is Group D.

### Group D — Deployment and operations hardening (D0/D1/D2/D3/V1 DONE; D4 steps 1-6 DONE; D4 itself not done)

Items:
- `ROADMAP.md:627` **TLS support**.
- `ROADMAP.md:176` **Docker image & container cleanup / consolidation**.
- `ROADMAP.md:632` **Backup/restore** — also the Group D tracker bullet in `ROADMAP.md`.
- `ROADMAP.md:652` **Docker Hub / GHCR images**.
- `ROADMAP.md:653` **ARM + AMD64 multi-arch CI**.
- Related one-time task: **D0 — publish source to GitHub** — COMPLETE 2026-11-19 (`https://github.com/michaelpeele/curatarr`, commit `8cc8cd3`); not a ROADMAP line.
- Related follow-on task: **D4 — blue/green production with sandbox** (new; approved after D2; not a ROADMAP line).

Why grouped: same release/deployment surface; mostly docs and compose/CI changes, no product UI.

Analysis doc: `.project_documents/PLAN-deployment-operations-hardening.md` (status **APPROVED SEQUENCE**). Approved order: **D0 GitHub source publication → D1 local ops basics → D3 registry/multi-arch CI → D2 TLS/reverse proxy → D4 blue/green production with sandbox**. D2 was explicitly moved to the end of the original Group D items; D4 follows it because blue/green depends on the routing/TLS decisions.

V1 release bundle (post-D3, DONE 2026-11-19): added `docker-compose.release.yml` using the two published GHCR images and documented a one-file/one-command install. Tag `v1.0.0` was pushed, workflow run `44449305935` succeeded, `1.0.0`/`1.0`/`latest`/`sha-0c28d00` were verified for both images on `linux/amd64` and `linux/arm64`, and a scratch release-compose stack was verified on port 15003 without touching production. Kept two images for V1; an all-in-one image is logged as a future unchecked ROADMAP item at the end of the roadmap. This did not start D2.

Current step: **D4 step 6 — scratch rehearsal (DONE 2026-11-19; user-approved)**. Scratch-only blue/green plus edge rehearsal passed deploy/smoke/flip/rollback, public checks, write guardrails, and teardown without a prod routing change. Prior step: **D4 steps 4-5 — data/auth guardrails (DONE 2026-11-19)**. Slot `/output` mounts default to `ro` via `OUTPUT_MOUNT_MODE`; D4 modes set `rw` only on the active slot. Restore keeps `chown -R 1654:1654 /data` plus `data-protection-keys` copy, and proxy-facing slots enforce `COOKIE_NAME=curatarr.auth`. Validated read-only with `bash -n`, help, `--status`, `--deploy-standby --check`, slot Compose config variants, and `git diff --check`; no slot started, routing unchanged, production untouched. Prior step: **D4 step 3 — promote.sh status/deploy/smoke/flip/rollback modes (DONE 2026-11-19)** was validated read-only with `bash -n`, help output, `--status`, and `--deploy-standby --check`; no slot was started, routing was unchanged, and no cutover was performed. Step 6 scratch rehearsal is DONE; teardown briefly stopped prod containers, prod was restored to HTTP 200, and edge now uses the isolated `curatarr-d4-edge` Compose project. D4 itself remains not done. Scope was corrected after analysis: this is real production for roughly 3–4 users, with scheduled maintenance windows and user-directed flip/rollback; sandbox verification remains the mandatory first gate. D2 TLS/reverse proxy is DONE (documentation + sandbox validation only): `.project_documents/TLS-REVERSE-PROXY.md` was added, README wording now points to Tailscale HTTPS, and a scratch HTTPS proxy in front of sandbox `:5174` verified app/API/SignalR access. Per the user's D2 scope decision, production TLS/cutover was not applied and no prod promotion was performed. D1 local ops basics are DONE and promoted; D3 registry/multi-arch CI is DONE; the V1 release bundle is DONE. The analysis is in `.project_documents/PLAN-deployment-operations-hardening.md`.

### Group E — Scale and security plumbing

Items:
- `ROADMAP.md:628` **Multi-root scanning**.
- `ROADMAP.md:644` **PostgreSQL backend**.
- `ROADMAP.md:648` **API token auth**.

Why grouped: backend/data-path work with broader integration impact. Treat each as its own batch; do not combine API token auth with DB provider changes.

### Group F — Watch state and external automation

Items:
- `ROADMAP.md:629` **Watch state tracking**.
- `ROADMAP.md:643` **Plex/Jellyfin integration**.
- `ROADMAP.md:645` **Webhooks**.
- `ROADMAP.md:201` **Library suggestions**.

Why grouped: watch state is the shared data model; integration/automation features consume it. Suggested order: watch state → Plex/Jellyfin → webhooks → suggestions.

### Group G — Onboarding and standalone long-term projects

Items:
- `ROADMAP.md:630` **User onboarding polish**.
- `ROADMAP.md:642` **MetaDatarr**.
- `ROADMAP.md:655` **i18n (non-English UI)**.

Why grouped only for visibility: these are independent; each needs its own planning doc. MetaDatarr is a new project, not a Curatarr sprint item.

## First item / next item

All six mandated batches from `workflow.md` are complete, including batch #6 (Calendar). Group B and its follow-up are done and promoted; Group C was closed in docs only because Calendar already delivers the next-episode goal and backdrops are not needed. In Group D — Deployment and operations hardening, D0 GitHub source publication is COMPLETE, D1 local ops basics is DONE and promoted, D3 registry/multi-arch CI is DONE after a successful CLI-triggered workflow run and manifest verification, and the V1 release bundle is DONE with tag `v1.0.0` and verified GHCR manifests. The approved order remains D0 → D1 → D3 → D2 TLS/reverse proxy (DONE; documentation plus sandbox validation only, no prod cutover) → D4 blue/green production with sandbox (steps 1-2 DONE; step 3 promote modes DONE (validated read-only; no cutover); steps 4-5 data/auth guardrails DONE (validated read-only; no cutover); step 6 scratch rehearsal DONE (no prod cutover; prod restored after teardown bug). D4 itself remains not done. Corrected scope: real-production slots for roughly 3–4 users, with scheduled maintenance windows and user-directed flip/rollback after sandbox verification and a mechanics rehearsal). See `.project_documents/PLAN-deployment-operations-hardening.md`. Analysis docs for completed groups B and C are `.project_documents/PLAN-library-grid-ui-polish.md` and `.project_documents/PLAN-metadata-show-enrichment.md`. The old first-item prompt in `.project_documents/PROMPT-first-remaining-item.md` is historical only.
