# CHECKPOINT — Fix 3 Runbook Execution
Date: 2026-11-08 11:XX local
Operated by: Project Manager (delegated to DevOps Architect subagent)
Repo: /codebase/Curatarr
Purpose: Apply Fix 3 runbook one step at a time. Each step recorded here with
explicit PASS/FAIL/INCONCLUSIVE. Resume from any later step if timeout/failure occurs.
NEVER skip steps. NEVER parallelize steps. STOP at the point of failure, mark that
step, and ask for the next.

---

## Pre-flight verification (documented before execution starts)
These were checked in the sandbox BEFORE delegation to DevOps Architect. They
reveal two pending items that belong in the bundle but are NOT blocking Fix 3.
Recorded here so they don't disappear when we return from any resumption.

### Preflight A — `docker-compose.yml` current state
- Header comment present (lines 1-8) describing volume replacement scheme.
- `api.environment`: `Frontend__BaseUrl=${FRONTEND_BASE_URL:-http://localhost:5173}` ✅ added in Fix 9's companion edit — wait, no: looking again, Frontend__BaseUrl IS in the compose file as currently written ✅
- `api.healthcheck`: real `wget` probe of `/api/bootstrapped`, 30s interval ✅ (P1-9 — deliverable, not yet committed separately but present)
- `volumes.app-data`: named, auto-create, NO external:true ✅ (P0-4 documented)
- `tailscale` service: bind mount `./${APP_NAME}-tailscale-data:/var/lib/tailscale`, `TS_SERVE_CONFIG=/var/lib/tailscale/serve.json` ✅

### Preflight B — on-disk `curatarr-tailscale-data/serve.json` content
Confirmed: the file contains:
```
{
  "TCP": { "443": { "HTTPS": true } },
  "Web": { "${TS_CERT_DOMAIN}:443": { "Handlers": { "/": { "Proxy": "http://127.0.0.1:80" } } } },
  "AllowFunnel": { "${TS_CERT_DOMAIN}:443": true }
}
```
Fix 3 doc claims it was "written cleanly" as a plain HTTP proxy `{"Web":{"5173:80": {...}}}`.
**This is a content mismatch.** The on-disk file still uses the cert-domain
placeholder (`${TS_CERT_DOMAIN}`) and targets `:443`, not `:80`. Does NOT break
local HTTP operation (Tailnet listens on 443; NAT maps `:80→:5173` independently);
but it doesn't match the Fix 3 doc's claim. **Report to user as a deferred
discrepancy, include in Fix 4 doc review, do NOT rewrite during Fix 3.**
Owner: User decides whether the 443 pattern stays. Risk: Tailnet-HTTPS route
breaks if cert domain never resolves — but for local HTTP testing it's fine.

### Preflight C — `Curatarr.Web/Dockerfile` `COPY nginx.conf /etc/nginx/conf.d/default.conf`
**Preflight finding (PENDING in bundle — Fix 7/8):** The root `nginx.conf` is a
FULL config (worker_processes / events / http{}). Copying it into `conf.d/` makes
nginx fatal (duplicate config source) on image build. Compose mounts the same file
over `/etc/nginx/nginx.conf` (full config), so at runtime compose wins. Build
would fail, runtime succeeds. **Not blocking Fix 3.** Bundle includes P1-7+P1-8.

---

## Runbook (exact order, no deviations)
Location on Pi: cd /codebase/Curatarr
Commands below MUST each complete and return non-zero exit ONLY as step FAILURE
(we will document that failure, stop, ask user). Any intermediate state preserved.

### Step 0 — Record current process list snapshot
```bash
cd /codebase/Curatarr
ps -ef | grep -E 'curatarr-api|ts-curatearr|tailscale' | grep -v grep
docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}'
docker volume ls
```
Expected (pre-run): curatarr-api, curatarr-web, ts-curatearr running; volumes curatarr-data, curatarr_curatarr-data exist.

### Step 1 — Stop manually-created containers
`docker rm -f curatarr-api curatarr-web ts-curatearr`
Expected: exit 0, no output, containers gone.
Document: `PASS` | fail: `...` | note: if `docker rm -f` returns exit >0 STOP, this step may have caused irreversibility — back up container configs first via `docker inspect` snapshots if needed.

### Step 2 — Remove old host volumes
`docker volume rm curatarr-data curatarr_curatarr-data`
Expected: exit 0, no output. NO DB retention (user confirmed).

### Step 3 — Remove stale images
`docker rmi curatarr-api curatarr-web`
Expected: exit 0, no output. (curatarr-api gets rebuilt by compose --build in step 5.)

### Step 4 — Start compose stack with fresh build
`docker compose up --build -d`
Parenthesized options: `--build` forces rebuild of api + web from Dockerfiles.
Compose auto-creates the new `app-data` volume for api.
Exit 0 expected. Verify services: `docker compose ps` → all three Up.

### Step 5 — Content root verification
`docker compose exec api env | grep ASPNETCORE_CONTENTROOT`
Expected: `ASPNETCORE_CONTENTROOT=/app/data` ✅
Note: Does NOT set MEDIA_ROOT/OUTPUT_ROOT/Frontend__BaseUrl explicitly?? YES it does — see compose. All present.

### Step 6 — Fresh DB / wizard trigger verification
`curl -s http://localhost:5173/api/bootstrapped`
Expected JSON: `{"isBootstrapped":false}` (fresh DB, no users) ✅
Side check: `docker compose exec api dotnet Curatarr.Api.dll --help` quickly confirms entrypoint reached (optional sanity).

### Step 7 — Tailnet connectivity
`docker exec ts-curatearr tailscale status`
Check presence status text = online. Optional: `docker exec ts-curatearr curl -s --max-time 5 http://127.0.0.1:80` returns nginx SPA HTML. If offline → INCONCLUSIVE per prior QA — escalate to user.
NOTE: keep this OPTIONAL for Fix 3. The runbook is the fix; Tailnet is secondary. If `tailscale status` fails, log it but do NOT block on it; document as deferred finding.

### Step 8 — Post-wizard persistence test
1. Complete the bootstrap wizard once via the UI (or POST to api endpoints manually if curl-only).
2. Restart the api service: `docker compose restart api`
3. Recheck: `curl -s http://localhost:5173/api/bootstrapped`
Expected after restart: `{"isBootstrapped":true}` ✅ Wizard must NOT reappear.
Note: If wizard reappears, root cause likely still volume mismatch → STOP, flag, tell user to confirm with us before next attempt.

---

## Step Results (live log)

| Step | Result | Details |
|------|--------|---------|
| 0 — Snapshot | ✅ PASS | 3 manual containers found (curatarr-api Up 5w, curatarr-web Up 10d, ts-curatearr Up 10d). Volumes: `curatarr-data`, `curatarr_curatarr-data` confirmed present. Three additional out-of-scope volumes noted: `curatarr-frontend-dist`, `curatarr-frontend-dist-new`, `curatarr-nginx-confd` — untouched. |
| 1 — docker rm -f | ✅ PASS | `curatarr-api` and `curatarr-web` stopped+removed (exit 0). `ts-curatearr` reported `No such container` (was auto-torn-down when shared-netns sibling `curatarr-web` was removed). No impact — verified absent from `docker ps`. |
| 2 — docker volume rm | ✅ PASS | `curatarr-data` and `curatarr_curatarr-data` both removed (exit 0). No in-use warnings. Absent from `docker volume ls`. Three out-of-scope volumes remain untouched. |
| 3 — docker rmi | ⚠️ PARTIAL PASS — USER DECISION PENDING | `docker rmi curatarr-api curatarr-web` succeeded (exit 0), deleted `:latest` short-name tags only. Four stale image entries REMAIN: `curatarr-api:step2`, `curatarr-web:step3`, `curatarr-curatarr-api:latest`, `curatarr-curatarr-web:latest`. All ~2 months old (pre-fix build era). Compose step 4 will build NEW `curatarr-curatarr-api:latest` and `curatarr-curatarr-web:latest` — orphans won't block the build but leave stale layer remnants. Decision: (a) remove all four → clean state, or (b) leave — cosmetic only. Awaiting user direction. |
| 4 — docker compose up --build -d | ⛔ BLOCKED — execution channel unavailable | `.dockerignore` patched to exclude `curatarr-tailscale-data/` (permission-denied fix). `docker compose` plugin installed in sandbox CLI (`docker compose version` → v2.27.1 exit 0). Static pre-flight audit of Step 4 passes: api build inputs correct (`ASPNETCORE_CONTENTROOT=/app/data` baked into image, no compose override), context transfer no longer blocked. Known risk (Preflight C): `Curatarr.Web/Dockerfile` copies full `nginx.conf` into `/etc/nginx/conf.d/default.conf` (invalid at image level, runtime mount overrides) — may cause `web` image build to fail even though compose command returns exit 0. DevOps subagent lost shell execution channel (`run_command_mcp_shell` unavailable); no docker commands can be executed from current position. |


## Resume instructions (for Project Manager if DevOps Architect times out mid-runbook)
After any step failure, OPEN this checkpoint file, mark the failing step FAIL, stop,
present the error message to user, and ask for continuation instruction. Do NOT
assume the underlying issue resolved itself. If partial state exists (e.g., step 4
built successfully but step 5 failed), you may re-run the failing step in isolation.
Never proceed beyond the FIRST failing step.

## Outcome append
On completion of ALL 8 steps: append summary line at top:
`# Fix 3 runbook completed: 0 failures, X step(s) verified PASS, Y deferred.`
Then prompt user: `next fix = Fix 1 (.env.example rewrite)`
