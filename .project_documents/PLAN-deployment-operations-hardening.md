# Plan — Deployment and operations hardening (Group D)

**Status: APPROVED SEQUENCE (2026-11-19); D2 TLS/reverse proxy DONE (2026-11-19).** D0 is COMPLETE: the source is published at `https://github.com/michaelpeele/curatarr` (initial commit `8cc8cd3 Initial import of Curatarr`, `main` pushed and tracking `origin/main`). D1 (local ops basics) is DONE and promoted at 2026-11-19T01:52:03Z. D3 (registry and multi-arch CI) is DONE: commit `d9ad777` added the GHCR workflow, the manual run `44442469640` succeeded, and both image manifests were verified for `linux/amd64` and `linux/arm64`. The V1 release bundle is DONE: commit `0c28d00` added `docker-compose.release.yml`, the `v1.0.0` tag was pushed, workflow run `44449305935` succeeded, and both GHCR images were verified at `1.0.0`, `1.0`, `latest`, and `sha-0c28d00` for `linux/amd64` and `linux/arm64`. A scratch release-compose stack was verified on port 15003 without touching production. D2 (TLS/reverse proxy) is DONE with the user-approved scope **documentation plus sandbox validation only**; the guide was added and validated through a scratch HTTPS proxy in front of sandbox, while prod TLS/cutover was intentionally not applied. No application code changed for V1, so no rebuild, sandbox rerun, or prod promotion was needed. The Backup/restore, Docker Hub/GHCR, and ARM+AMD64 CI ROADMAP items were marked done only after their verification. D4 (blue/green) analysis has been corrected to real-production blue/green with scheduled maintenance windows and user-directed flip/rollback. D4 steps 1 and 2 are DONE: the parameterised slot Compose template/env examples and the host edge proxy/Caddyfile with flip/rollback mechanism were added and validated. User approved step 3; `promote.sh` status/deploy/smoke/flip/rollback modes are DONE (2026-11-19): modes added and validated read-only (`bash -n`, help output, `--status`, `--deploy-standby --check`); no slot was started and no cutover was performed. User approved D4 steps 4-5 (data/auth guardrails) on 2026-11-19; they are DONE: slot output mounts default read-only via `OUTPUT_MOUNT_MODE`, D4 modes set `rw` only on the active slot, restored standby DB checks retain `chown -R 1654:1654 /data` plus `data-protection-keys` copy, and proxy-facing slots enforce `COOKIE_NAME=curatarr.auth`. Validation was read-only (`bash -n`, help, `--status`, `--deploy-standby --check`, slot Compose config variants, `git diff --check`); no slot started, no cutover. Step 6 scratch rehearsal remains gated for final approval. D4 itself remains not done; cutover is not approved or performed. D4 was added after D2 per user approval.

## Scope

This analysis covers Group D from `.project_documents/REMAINING-WORK-GROUPS.md`. The roadmap items are:

1. `ROADMAP.md:627` — **TLS support** (reverse proxy + Let's Encrypt guide).
2. `ROADMAP.md:176` — **Docker image & container cleanup / consolidation** (explicit prod tags; single-container option).
3. `ROADMAP.md:632` — **Backup/restore** (SQLite dump/restore docs).
4. `ROADMAP.md:652` — **Docker Hub / GHCR images**.
5. `ROADMAP.md:653` — **ARM + AMD64 multi-arch CI**.

There is also one related one-time task requested by the user: **D0 — publish the source code to GitHub**. This is not a `ROADMAP.md` line and is not the same as D3 image publishing; it is the prerequisite for any future GitHub Actions/CI work.

These share the release/deployment surface. None changes product UI or the media data path.

## Current state

### Deployment topology

- Prod `docker-compose.yml` builds `api` and `web` locally from service-level `build` blocks. Container names are `${APP_NAME}-api` / `${APP_NAME}-web`; `api` publishes `${API_LOCAL_PORT}:5000`; `web` uses `network_mode: service:tailscale`.
- The Tailscale sidecar uses `tailscale/tailscale:latest` with `TS_AUTHKEY`, `TS_SERVE_CONFIG`, and `./${APP_NAME}-tailscale-data:/var/lib/tailscale`. `fix-03-stale-image-and-compose-migration.md` records that Tailscale serve currently maps host 5173 to plain HTTP `:80`.
- Sandbox `docker-compose.sandbox.yml` uses prebuilt `curatarr-api:latest` / `curatarr-web:latest`, ports 5001/5174, its own volume, isolated `/output`, cookie `curatarr.sandbox.auth`, and no Tailscale.
- `scripts/promote.sh` builds once, recreates/verifies the sandbox (`smoke`, `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`), then deploys to prod with `docker compose up -d --no-build api web` and smokes `:5000`. Modes: `full`, `--sandbox-only`, `--prod-only`; log: `logs/promotions.log`.
- API image: .NET 10 SDK → aspnet runtime, non-root uid 1654, SQLite at `/app/data/curatarr.db`, `ASPNETCORE_URLS=http://0.0.0.0:5000`.
- Web image: Node 22 build → nginx 1.27 runtime, static SPA, `/api` and `/hubs` proxied by `Curatarr.Web/nginx.conf`.

### Documentation drift already noticed

- `README.md` drift was corrected during D1: its config table now uses `WEB_LOCAL_PORT`/`API_LOCAL_PORT` and the current host-path variables instead of the old `WEB_PORT`/`API_PORT` names.
- `.project_documents/DOCKER_FIXES.md` still lists `.env.example` sync and several Docker fixes as pending.
- `REMAINING-WORK-GROUPS.md` has stale refs for TLS/backup/registry/multi-arch and a stale final “next group” section. These will be corrected as part of documentation prep, not treated as product work.

### Backup/restore precedent

Promotion runbooks already use a high-value manual gate: online SQLite backup (`sqlite3 /data/curatarr.db '.backup /backup/curatarr.db'`), copy WAL if present, restore-check volume, boot a scratch API image against the restored DB on port 15001, then remove scratch resources. `CHECKPOINT.md` records that this passed historically. There is no general operator-facing backup/restore doc yet.

## Approved order

**Do not implement all items as one batch.** They have different risk profiles and prerequisites. The approved order is **D0 → D1 → D3 → D2 → D4**. D2 remains the last of the original Group D items; D4 follows it because blue/green depends on the reverse-proxy/TLS routing work.

### Sub-batch D0 (COMPLETE 2026-11-19) — publish source to GitHub

Completed:

- Repo published publicly at `https://github.com/michaelpeele/curatarr`.
- Initial commit `8cc8cd3 Initial import of Curatarr`; `main` pushed and tracking `origin/main`.
- GitHub CLI v2.105.0 installed at `/home/michaelpeele/.local/bin/gh`; user authenticated as `michaelpeele`; `gh auth setup-git` configured Git credential integration.
- Pre-commit cleanup excluded `.env`, secrets, Tailscale state, local data, logs, DBs, generated build files, stale backups, and root duplicate configs. No live secrets or runtime data were published.
- Functional test scripts were left unchanged per user decision; personal emails in docs were sanitized to generic test addresses.

Why first: the user explicitly asked to publish the source, and a GitHub remote is a prerequisite for any later CI/image publishing. This was repository setup only; it is not D3.

### Sub-batch D1 — local ops basics

Include:

- Prod compose image tagging cleanup (`ROADMAP.md:176`): stop relying on implicit local `latest` for prod. Use explicit, versioned image names/tags in compose while preserving the shared source tree and the existing promote flow. Keep `curatarr-api:latest` / `curatarr-web:latest` for sandbox unchanged.
- Operator backup/restore guide (`ROADMAP.md:632`): generalise the existing promotion gate into a documented, repeatable procedure, including online backup, WAL handling, verification by restoring to a scratch API on port 15001, and rollback notes.
- Reconcile README/env/compose port-name drift and add a pointer to the backup guide. This is documentation/config consistency, not a new feature.

Why second: it is low risk, uses procedures already proven in promotions, and improves safety before any deployment changes.

### Sub-batch D3 — registry and multi-arch CI

Include:

- `ROADMAP.md:652` Docker Hub / GHCR images.
- `ROADMAP.md:653` ARM + AMD64 multi-arch CI.

Why here: D0 supplies the GitHub remote. Image publishing still needs credentials, repository secrets, CI configuration, and a tag/release policy. Do this before TLS when there is a concrete distribution need, but do not fold it into D0 or D1.

### Sub-batch D2 (last) — TLS / reverse proxy

Include (`ROADMAP.md:627`): document a supported HTTPS setup. Recommend leading with the existing Tailscale path (HTTPS via Tailscale serve/funnel or an HTTPS-enabled reverse proxy in front of `web`) and include a Let's Encrypt example (Caddy or nginx) for non-Tailscale installs. Keep the app itself HTTP-on-5000 behind the proxy; do not terminate TLS in .NET.

Why last of the original items: the user asked to move TLS to the end after all other original Group D items are completed. TLS setup depends on the operator's network and public DNS and is mainly a guide plus optional compose profile/example, not a product change.

### Sub-batch D4 (new; after D2) — blue/green production with sandbox

Include:

- Parameterise container names, ports, DB volumes, and Tailscale state so sandbox plus two production slots (blue/green) can coexist on one machine.
- Keep media read-only and give `/output` active-only writes or separate paths. Only the active production stack owns the writable SQLite DB; the standby verifies from a restored copy.
- Add `promote.sh` modes for deploy-standby, smoke-test, flip, and rollback. Use a host reverse proxy as the switch point (approved over flipping the Tailscale funnel port) so it pairs cleanly with D2 TLS.
- Treat cookie/auth secret continuity and a brief write freeze at cutover as explicit requirements.

Why after D2: blue/green needs the backup discipline from D1 and the reverse-proxy/TLS routing decisions from D2. It adds real operational complexity (three isolated stacks, more disk, more container plumbing), so it follows all other Group D work.

#### D4 analysis — specific next steps (2026-11-19; analysis only)

No code has been changed for D4. Scope was corrected by the user after the first analysis: this deployment is **real production**, with roughly 3–4 users and scheduled maintenance windows. D4 should therefore implement blue/green as the **actual production slot layout**, not merely a scratch rehearsal. Sandbox verification stays the mandatory first gate; the standby production slot is deployed and smoke-tested, the user tests it, and the user directs flip or rollback during an approved maintenance window. Full zero-downtime is not claimed; the value is a reversible, well-logged cutover.

**Proposed slot layout (confirm before implementation):**

| Concern | Sandbox (unchanged) | Blue slot | Green slot |
| --- | --- | --- | --- |
| Compose project | `curatarr-sandbox` | `curatarr-blue` | `curatarr-green` |
| API container | `curatarr-sandbox-api` | `curatarr-blue-api` | `curatarr-green-api` |
| Web container | `curatarr-sandbox-web` | `curatarr-blue-web` | `curatarr-green-web` |
| API host port | `5001` | `15100` | `15200` |
| Web host port | `5174` | `15173` | `15273` |
| DB volume | `curatarr-sandbox-data` | `curatarr-blue-data` | `curatarr-green-data` |
| Output dirs | isolated sandbox dirs | `/srv/curatarr/blue/output` and `/srv/curatarr/blue/VirtualLibraries` | `/srv/curatarr/green/output` and `/srv/curatarr/green/VirtualLibraries` |
| Media | read-only host media dir | shared read-only | shared read-only |

Each slot gets its own Compose network and an internal network alias named `curatarr-api`; this is required because `Curatarr.Web/nginx.conf` hardcodes `proxy_pass http://curatarr-api:5000`. The containers can still be named `curatarr-blue-api` / `curatarr-green-api` at the host level.

**Proposed files and state:**

- `docker-compose.slot.yml`: one parameterised API/web template used twice with different env/state. No Tailscale service in each slot.
- `configs/slot-blue.env.example`, `configs/slot-green.env.example`, `configs/edge.env.example`: committed examples; real `.env.*` files stay ignored.
- `docker-compose.edge.yml` plus a generated Caddyfile: host reverse proxy on `80`/`443`, upstream is the active slot web port (`15173` or `15273`).
- `state/active-slot`: small ignored state file containing `blue` or `green`. All promote modes read/write it through one helper.
- `.gitignore`: add `state/` if not already ignored.

The existing single-stack `docker-compose.yml`, Tailscale sidecar, `5000`, and `5173` are the current production layout and remain in place until the approved D4 cutover. D4 implementation replaces the exposed stack with the two production slots plus the approved host reverse proxy as the switch point. Tailscale users should reach the same HTTPS hostname/route rather than flipping a per-slot Tailscale funnel; no Tailscale sidecar is added inside either slot.

**Implementation steps, in order:**

1. Add the parameterised `docker-compose.slot.yml` and env examples. Validate both slots with `docker compose -f docker-compose.slot.yml --env-file ... config --quiet` and confirm there are no container, port, volume, or output-path collisions with sandbox or the current prod stack.
2. Add the host edge proxy files. Caddy is recommended because D2 already documents it. The Caddyfile should proxy `{$CURATARR_DOMAIN}` to the active upstream; a flip rewrites that upstream and runs `caddy reload` rather than restarting API/web containers.
3. Extend `scripts/promote.sh` with explicit modes while leaving existing `full`, `--sandbox-only`, and `--prod-only` behavior intact:
   - `--status`: show active slot, standby slot, container health, and proxy upstream.
   - `--deploy-standby`: recreate only the standby slot from verified images.
   - `--smoke-standby`: smoke the standby directly on its API/web ports and verify DB integrity/row counts against a restored copy.
   - `--flip --confirm-cutover`: final snapshot, brief write freeze, copy DB plus `data-protection-keys`, refresh standby, verify, switch proxy, smoke public URL, update active-slot marker.
   - `--rollback --confirm-cutover`: switch proxy back to the previous slot and smoke it.
   Each mode logs active/standby slots and result to `logs/promotions.log`.
4. Make the data rules explicit in the script: media is read-only; only the active slot mounts `/output` writable; the standby verifies against its own restored DB volume; after copying a SQLite backup into a slot volume, run `chown -R 1654:1654 /data` because the API runs as uid 1654.
5. Preserve auth continuity: keep the public cookie name (`curatarr.auth`) and copy `/app/data/data-protection-keys` with the DB during standby refresh. Direct slot testing may use slot-specific cookie names, but the proxy-facing hostname must not change cookies at cutover.
6. Rehearse the mechanics on scratch resources first: start blue and green plus the edge proxy on scratch ports/hostnames; deploy standby while active is unchanged; smoke standby; rehearse flip and rollback. Then use the same deploy/smoke/user-test/flip-or-rollback sequence for the real production slots during an approved maintenance window. Verify login, writes, `/api/`, `/hubs/`, and invite/reset links. Record results in `PROGRESS.md`.
7. Treat production cutover as part of D4, but only after explicit user approval of the maintenance window, DNS/hostname, final snapshot, and go/no-go. The user directs flip or rollback after testing the standby; no automatic cutover.

**Decisions to confirm before implementation:**

1. Approve the slot names, ports, volumes, and output paths above.
2. Approve D4 scope as real-production blue/green for roughly 3–4 users, with scheduled maintenance windows and user-directed flip/rollback; sandbox verification and a mechanics rehearsal remain mandatory before cutover.
3. Approve host Caddy as the switch point and keep Tailscale sidecar out of the slot stacks.

Recommendation: accept all three. After approval, implementation can start at step 1; no ROADMAP item is marked done until the production cutover and verification pass.

## Suggested implementation detail

### D0 — GitHub source publication

- Treat the current directory as untracked source: check for existing `.git`, review the full file list, and confirm no credentials or runtime data are staged.
- Add or refine `.gitignore` for `.env`, Tailscale state (`*-tailscale-data/`), local databases/backups, logs, build output, and editor/OS files.
- `git init`, create the initial commit, add the user-provided GitHub remote, and push the default branch.
- Record the GitHub URL in project docs; do not put tokens in the repository.

### D1 — local ops basics

- No C# or React changes expected.
- `docker-compose.yml`: replace implicit local build-only images with explicit tags (for example `curatarr-api:${CURATARR_VERSION:-latest}` / `curatarr-web:${CURATARR_VERSION:-latest}`), keeping build blocks so `promote.sh` still builds from the shared tree. Verify `promote.sh --sandbox-only` and `--prod-only` still select the intended images.
- Add `.project_documents/BACKUP-RESTORE.md` (or equivalent) derived from the existing promotion gate; link it from `README.md` and `REMAINING-WORK-GROUPS.md`.
- Fix README config table key names and `.env.example` drift as a small docs/config cleanup.
- No schema changes, no new endpoints, no UI changes.
- Implementation complete (2026-11-19): image tags and `TS_EXTRA_ARGS` were added to `docker-compose.yml`; `.env.example` now matches the actual compose keys; `BACKUP-RESTORE.md` was added and linked from `README.md`, `.project_documents/README.md`, `REMAINING-WORK-GROUPS.md`, and the ROADMAP D1 tracker. Sandbox verification, backup/restore rehearsal, prod promotion, and bundle verification all passed; see `PROGRESS.md`.

### D2 — TLS / reverse proxy (DONE 2026-11-19)

Scope confirmed by the user: **document and validate on sandbox only**. Do not change the live production stack, do not promote, and do not require a public DNS/domain decision. Curatarr remains HTTP behind the proxy; TLS terminates at Tailscale or at the reverse proxy, never in .NET.

Implementation complete: `.project_documents/TLS-REVERSE-PROXY.md` documents Tailscale HTTPS, Caddy + Let's Encrypt, nginx/Certbot, and Traefik equivalents. README wording was updated to the Tailscale HTTPS URL and links the guide. A scratch Caddy proxy with a self-signed certificate was validated in front of sandbox `:5174`: HTTPS root/API access worked, login worked, and authenticated SignalR negotiation returned 200. The expected unauthenticated `/api/me` 401 was the only console message; there were no application errors. Scratch resources were removed and production was untouched.

- Add `.project_documents/TLS-REVERSE-PROXY.md` as the supported operator guide.
- Lead with the existing Tailscale HTTPS path. The current `curatarr-tailscale-data/serve.json` maps `${TS_CERT_DOMAIN}:443` HTTPS to local port 80; update README wording from plain `http://curatarr` to the Tailscale HTTPS URL while noting plain HTTP remains available only where explicitly configured.
- Include a Caddy + Let's Encrypt example for public-domain installs, with `reverse_proxy 127.0.0.1:5173` in front of the released Compose web service. Mention nginx/Traefik equivalents without committing to a full config for each.
- Document that `/api/` and `/hubs/` are already proxied by the web container to service `curatarr-api`; the external TLS proxy only needs to target the web port.
- Validate with a scratch HTTPS reverse proxy in front of sandbox `web` on `:5174`, using a self-signed certificate for the test. Verify the app loads over HTTPS, API/SignalR paths proxy correctly, and the browser console has no errors. Remove the scratch proxy afterward.
- Mark `ROADMAP.md:627` done only after the guide and sandbox validation pass, with an explicit note that production TLS cutover was intentionally not applied.

### D3 analysis — registry and multi-arch CI (DONE 2026-11-19)

#### What this is

D3 is about publishing Curatarr container images to a registry and automating builds from GitHub.

- **Docker Hub / GHCR images** (`ROADMAP.md:652`): instead of every install building `curatarr-api` and `curatarr-web` locally, CI publishes named images such as `ghcr.io/michaelpeele/curatarr-api:1.2.3` and `curatarr-web:1.2.3`. Operators can then pull a specific release instead of compiling from source.
- **ARM + AMD64 multi-arch CI** (`ROADMAP.md:653`): a GitHub Actions workflow uses Docker Buildx (with QEMU emulation where needed) to build the same image for `linux/amd64` and `linux/arm64`, then publishes one multi-platform manifest tag. A single `docker pull` gets the right architecture automatically.
- **CI** means the build/publish happens in GitHub Actions on a tag, release, or manual dispatch, with credentials stored as repository secrets. It is release infrastructure, not an application feature.

D0 created the GitHub remote needed for this. D1 made local image tags explicit. D3 does **not** require changing app code, the database, or the existing local `promote.sh` flow unless we later choose to consume registry images in production.

#### Why do it

- Easier installs and upgrades: users pull a versioned image instead of building locally.
- Reproducible releases: the published artifact matches a Git tag/release.
- Multi-platform support: Raspberry Pi/ARM and normal AMD64 hosts use the same tag.
- It creates a clean release boundary before D2 TLS and D4 blue/green work.

#### Recommended approach

1. Use **GHCR first**, because the source is already on GitHub and `GITHUB_TOKEN` can publish with limited extra setup. Docker Hub can be added later as a mirror if desired.
2. Publish separate `curatarr-api` and `curatarr-web` images.
3. Trigger on version tags (for example `v*`) and manual `workflow_dispatch`. Do not publish on every ordinary commit.
4. Tag each image with the semantic version, the full Git SHA, and `latest` only for the newest release.
5. Use Docker Buildx with platforms `linux/amd64,linux/arm64`; use QEMU only for the non-native architecture.
6. Keep CI as a separate publication path. The existing `docker compose build` / `promote.sh` sandbox-first workflow remains the source of truth for local promotion until a later decision says prod should pull registry images.
7. Add README guidance showing how to pull a published release and how to verify the architecture with `docker buildx imagetools inspect`.

#### Decisions to confirm

- Registry: **GHCR only first** (recommended) or also Docker Hub now.
- Visibility: **public images** (recommended, matching the public source) or private GHCR packages.
- Tag policy: **semver + SHA + `latest`** (recommended) or SHA-only.
- Trigger: **release/tag + manual dispatch** (recommended) or every push to `main`.
- Should local `promote.sh` switch to pulling registry images? Recommendation: **not in D3**; keep local builds and promotion unchanged.

#### Out of scope

- No application code, UI changes, schema changes, or endpoint changes.
- No change to current prod deployment behaviour.
- No TLS/reverse-proxy work (D2).
- No blue/green cutover work (D4).
- The newly logged Multiselect sticky-bulk-bar bug (`ROADMAP.md` Bugs #6) is separate UI work and must not be folded into D3.

#### D3 verification results

- Workflow YAML parsed locally; permissions use `contents: read` plus `packages: write` and the built-in `GITHUB_TOKEN` with no custom secret.
- Pushed `d9ad777 CI: publish multi-arch images to GHCR` and ran the workflow via CLI: `gh workflow run publish-images.yml --ref main`. Run `44442469640` completed successfully at 2026-11-19T02:20:48Z.
- Manual dispatch on `main` published SHA tags (`sha-d9ad777`) for both API and web images. Semver and `latest` remain tag/release-only by design.
- `docker buildx imagetools inspect ghcr.io/michaelpeele/curatarr-api:sha-d9ad777` and the matching `curatarr-web` tag both resolve to a single OCI index containing `linux/amd64` and `linux/arm64`.
- Pulled both published images locally; the web image references `assets/index-UEE7VSba.js` / `assets/index-fENfh4zq.css`, matching the current prod baseline. No app code changed, so no sandbox rerun or prod promotion was needed.
- Results were recorded in `.project_documents/PROGRESS.md` before the ROADMAP items were marked done.

#### D3 acceptance criteria

- The publishing workflow publishes API and web images to the chosen registry on its configured triggers; the verified manual dispatch produced SHA-tagged images.
- The published tag resolves for both AMD64 and ARM64 hosts.
- Registry credentials are stored only as GitHub secrets; no tokens are committed.
- Existing sandbox-first build/promotion behaviour remains intact.
- ROADMAP items `652` and `653` are marked done only after workflow verification.

### V1 release bundle (post-D3; DONE 2026-11-19)

User decision: ship with the two-image recommendation. Do **not** merge API and web into a single image for V1. Instead, add `docker-compose.release.yml` using the published GHCR images so users can install with one file and one command without cloning or building.

Details:

- API service must be named `curatarr-api`; `Curatarr.Web/nginx.conf` proxies `/api/` and `/hubs/` to that hostname.
- Use `ghcr.io/michaelpeele/curatarr-api:${CURATARR_RELEASE_TAG:-1.0.0}` and `ghcr.io/michaelpeele/curatarr-web:${CURATARR_RELEASE_TAG:-1.0.0}`. Keep the tag variable separate from local `CURATARR_VERSION` so a developer's `latest` value cannot change the released bundle.
- Keep two separate service blocks; Docker Compose cannot put two images in one service block. Document that this is why Sonarr/Radarr look different: those projects package backend and UI into one image/process.
- No `build:` blocks, no Tailscale sidecar, and no host API port publish in the release file. Expose the API on the Compose network and publish only the web UI port by default.
- Preserve the API configuration used by the source compose: `ASPNETCORE_ENVIRONMENT=Production`, `TMDB__ApiKey`, SMTP settings, `MEDIA_ROOT=/media`, `OUTPUT_ROOT=/output`, and `Frontend__BaseUrl`.
- Preserve the data/media/output volume layout, with a named `curatarr-data` volume and read-only media mounts.
- Document the release install path in README and add `CURATARR_RELEASE_TAG` to `.env.example`.
- Pushed `v1.0.0`; workflow run `44449305935` succeeded and published `1.0.0`, `1.0`, `sha-0c28d00`, and `latest`. Verified both GHCR images with `docker buildx imagetools inspect` (both platforms in each manifest), pulled the `1.0.0` images, and started a scratch release-compose stack on port 15003 with a unique volume. API became healthy, `/api/bootstrapped` returned `{"isBootstrapped":false}` on a fresh DB, web served `index-UEE7VSba.js` / `index-fENfh4zq.css`, and the first-boot wizard loaded with zero console errors. Scratch resources were removed; production was untouched. See `PROGRESS.md` for digests and details.

Future work: log a single all-in-one image as an unchecked ROADMAP item at the end of the current roadmap. It is explicitly not part of V1.

## Verification plan (mandatory, per `workflow.md`)

D0 was repository setup only and is complete: the remote, pushed commit, and exclusion of secrets/runtime data were verified. No Docker build or promotion was needed for D0.

For D1 and later deployment changes, even docs/compose work must be verified through the sandbox first:

1. `docker compose build api web`.
2. `./scripts/promote.sh --sandbox-only`; wait for `:5001/api/bootstrapped`; confirm smoke checks and `BASE=http://localhost:5001 WIZARD_ONLY=1 ./test-api.sh`.
3. Verify sandbox `:5174` still loads and the served bundle hash matches the built asset; browser console has zero errors.
4. Exercise the documented backup/restore procedure against sandbox data or a scratch copy, including the port-15001 restore verification, without touching prod.
5. Record manual verification in `.project_documents/PROGRESS.md`.
6. Only after sandbox/manual checks pass, run `./scripts/promote.sh --prod-only` if no code/config changed afterward; otherwise rerun sandbox verification first.
7. Hard-refresh prod and verify served JS/CSS hashes against the previous prod baseline. Current baseline: `index-UEE7VSba.js` / `index-fENfh4zq.css`.

D4 additionally requires a full cutover rehearsal on sandbox-like scratch resources before any real cutover: deploy standby, smoke-test it against a restored DB copy, flip the reverse proxy, verify cookies/auth and writes, then rehearse rollback. After that rehearsal passes, production cutover may be performed only in an approved maintenance window with user-directed flip or rollback.

Do not mark ROADMAP items done until after verification and promotion. Documentation-only closeouts (like Group C) are the exception and should say so explicitly.

## Decisions recorded

1. **D0 — publish source to GitHub** was added and is COMPLETE; D1, D2, D3, and the V1 release bundle are also done, and the next step is D4.
2. Run Group D in this order: **D0 → D1 → D3 → D2 → D4**.
3. Keep **D2 (TLS / reverse proxy)** as the last of the original Group D items; **D4 (blue/green)** follows it because it depends on the routing/TLS work.
4. Use a **host reverse proxy** as the blue/green switch point rather than flipping the Tailscale funnel port.
5. Treat blue/green as a separate D4 sub-batch, not part of D0–D3.

D0, D1, D2, D3, and the V1 release bundle are complete. D4 (blue/green production with sandbox) is the next step.
