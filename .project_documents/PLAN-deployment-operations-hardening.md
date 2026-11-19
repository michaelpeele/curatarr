# Plan — Deployment and operations hardening (Group D)

**Status: APPROVED SEQUENCE (2026-11-19).** D0 is COMPLETE: the source is published at `https://github.com/michaelpeele/curatarr` (initial commit `8cc8cd3 Initial import of Curatarr`, `main` pushed and tracking `origin/main`). D1 (local ops basics) is DONE and promoted at 2026-11-19T01:52:03Z. D3 (registry and multi-arch CI) is IN PROGRESS after user approval; its analysis is recorded below, the GitHub Actions workflow and README registry guidance have been added under the recommended GHCR-first defaults, and YAML validation passed. GitHub Actions verification and manifest checks are still pending. No app code was changed. Only the Backup/restore ROADMAP item was marked done after promotion; D3/D2/D4 remain pending. D4 (blue/green) was added after D2 per user approval.

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

### D3 analysis — registry and multi-arch CI (PROPOSED; awaiting approval)

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

#### D3 verification plan

- Validate the workflow YAML and confirm repository secrets/permissions are minimal.
- Run a manual workflow dispatch against a test tag.
- Confirm both API and web images are published.
- Use `docker buildx imagetools inspect <image>:<tag>` to verify both `linux/amd64` and `linux/arm64` manifest entries.
- Pull the published tag locally and confirm the app starts with the expected bundle hashes.
- If compose or scripts change, rerun the mandatory sandbox workflow; otherwise no prod promotion is needed because no served assets change.
- Record results in `.project_documents/PROGRESS.md` before marking any ROADMAP item done.

#### D3 acceptance criteria

- A tagged release publishes API and web images to the chosen registry.
- The published tag resolves for both AMD64 and ARM64 hosts.
- Registry credentials are stored only as GitHub secrets; no tokens are committed.
- Existing sandbox-first build/promotion behaviour remains intact.
- ROADMAP items `652` and `653` are marked done only after workflow verification.

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

D4 additionally requires a full cutover rehearsal on sandbox-like scratch resources: deploy standby, smoke-test it against a restored DB copy, flip the reverse proxy, verify cookies/auth and writes, then rehearse rollback. No production cutover without that rehearsal.

Do not mark ROADMAP items done until after verification and promotion. Documentation-only closeouts (like Group C) are the exception and should say so explicitly.

## Decisions recorded

1. **D0 — publish source to GitHub** was added and is COMPLETE; the next step is D1.
2. Run Group D in this order: **D0 → D1 → D3 → D2 → D4**.
3. Keep **D2 (TLS / reverse proxy)** as the last of the original Group D items; **D4 (blue/green)** follows it because it depends on the routing/TLS work.
4. Use a **host reverse proxy** as the blue/green switch point rather than flipping the Tailscale funnel port.
5. Treat blue/green as a separate D4 sub-batch, not part of D0–D3.

D0 is complete and published; no further action is needed for it. Implementation of D1 has not started.
