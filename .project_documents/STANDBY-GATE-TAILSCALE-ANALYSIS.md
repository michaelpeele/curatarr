# Standby gate via Tailscale — analysis only

Date: 2026-11-19. No code, compose, route, or docs status changed by this analysis.

## Decision

Yes, the standby can have its own stable name.

Recommended: tailnet-only Tailscale Service named `curatarr-standby`. Do not use a public Funnel for pre-flip gating. Funnel exposes the service to the public internet; a gate should stay inside the tailnet.

Use a public second Funnel only if gating must happen from outside the tailnet.

## Current production topology

- Public primary: `https://curatarr.tail0307d.ts.net`
- Edge proxy: `curatarr-edge`, host port `:5180`
- Active slot: green, upstream `127.0.0.1:15273`, `/output` rw
- Standby slot: blue, upstream `127.0.0.1:15173`, `/output` ro
- `state/cutover/active-slot=green`
- Old prod `api`/`web` stopped; Tailscale, edge, blue, green, sandbox remain running.

Current primary route was changed at runtime with `tailscale serve ...`. The persisted `curatarr-tailscale-data/serve.json` still points to the old local target `http://127.0.0.1:80`. Deployment must either persist the new route in `serve.json` or reapply it after container restart.

## Stable standby routing requirement

Do not name the gate after a slot color. After `flip`, `blue` and `green` swap roles. A name like `curatarr-green` becomes wrong.

Add a stable local standby endpoint, for example `:5181`. The edge config should render two upstreams:

- active endpoint: `:5180`
- standby endpoint: `:5181`

`flip` and `rollback` must swap both endpoints so the gate hostname always follows the standby role.

## Option A — recommended, tailnet-only Tailscale Service

Uses the existing Tailscale node. No new auth key.

Production deploy sequence:

1. Add a second Caddy site/listener for `:5181` pointing at the standby upstream.
2. Verify local standby smoke: `curl http://127.0.0.1:5181/api/bootstrapped`.
3. Verify standby write guard fails on `/output`.
4. Create a Tailscale service config:
   - file under ignored `state/` or `curatarr-tailscale-data/`
   - top-level `version: "0.0.1"`
   - service key `svc:curatarr-standby`
   - endpoint `"tcp:443": "http://localhost:5181"`
5. Apply with `tailscale serve set-config --service=svc:curatarr-standby <file>`.
6. Confirm exact DNS name with `tailscale serve status`.
7. Persist both primary and standby service config; verify after a Tailscale restart.
8. During `flip`/`rollback`, swap the Caddy standby upstream and reload edge. The service name stays stable.

## Option B — public second Funnel

Use only if the gate must be reachable from outside the tailnet.

Implementation:

- Add a second Tailscale node/container, e.g. `ts-curatarr-standby`.
- Give it hostname `curatarr-standby`.
- Configure its Funnel on `443` to target the standby endpoint, e.g. `http://172.22.0.1:5181`.
- Keep the primary node and Funnel untouched.

### Auth key placement

Generate a second Tailscale auth key in Tailscale admin.

- Reusable key: use if redeploy automation must recreate the node.
- One-time key: use for manual one-off setup.
- Put the secret only in ignored real `.env` as `TS_STANDBY_AUTHKEY=...`.
- Add only a blank placeholder to tracked `.env.example`.
- Put node state in ignored `curatarr-tailscale-standby-data/`.
- Compose service gets `TS_AUTHKEY=${TS_STANDBY_AUTHKEY:-}` and `hostname: curatarr-standby`.
- Never commit the key.

## Name choices

Recommended:

1. `curatarr-standby` — stable role name; survives flips.
2. `curatarr-gate` — clear purpose; stable role name.
3. `curatarr-preflip` — good for pre-cutover checks; slightly narrow.

Avoid:

- `curatarr-blue`
- `curatarr-green`

Slot colors swap after flip; those names become misleading.

## Important implementation caveat

`tailscale funnel --help` has no `--service` flag. Named Services are configured through `tailscale serve --service` / `tailscale serve set-config`, not through Funnel CLI. Verify whether a named Service can be Funnel-exposed before promising a public named service. The safe public design is a second Tailscale node/container with its own Funnel.

## Status

Analysis only. No implementation approved yet.

## External Funnel failure diagnosis (2026-11-19)

Symptom: Curatarr works from the host/tailnet but not externally through the expected Funnel URL.

Runtime evidence:

- `docker exec ts-curatarr tailscale serve status` reports `https://curatarr.tail0307d.ts.net (tailnet only)` with `/ proxy http://172.22.0.1:5180`.
- `docker exec ts-curatarr tailscale funnel status` reports the same route, not an active public Funnel.
- `tailscale serve status --json` and `tailscale funnel status --json` both show TCP 443 HTTPS and the proxy target, but no `AllowFunnel` field in runtime config.
- Host curl resolves the hostname to tailnet IP `100.109.84.44`; host `https://curatarr.tail0307d.ts.net/api/bootstrapped` returns 200. That proves tailnet access, not public Funnel access.
- `webfetch` from the assistant tool network also returned `{"isBootstrapped":true}`, but that network may resolve tailnet DNS. It is not proof of public Funnel availability.
- Persisted `curatarr-tailscale-data/serve.json` still points to the old target `http://127.0.0.1:80`; runtime and persisted config diverge.

Cause: D4-7 changed the public route with:

```bash
tailscale serve --bg --https=443 --yes http://172.22.0.1:5180
```

That command creates a tailnet-only Serve route. Its D4-7 output included `Removing Funnel for curatarr...`. It replaced the public Funnel with Serve. Tailscale Serve and Funnel cannot both own the same port; the most recent command wins.

Proposed fix, not applied:

```bash
docker exec ts-curatarr tailscale funnel --bg --https=443 --yes http://172.22.0.1:5180
docker exec ts-curatarr tailscale funnel status
```

Then persist the route so restart does not revert to `http://127.0.0.1:80`:

- Set `curatarr-tailscale-data/serve.json` target to `http://172.22.0.1:5180`.
- Keep `AllowFunnel` true for `${TS_CERT_DOMAIN}:443`.
- Verify both runtime status and persisted config.

Script fix to consider after approval: change `scripts/d4-cutover.sh` `route()` and `abort()` from `tailscale serve ...` to `tailscale funnel ...`, and update the generated persisted `serve.json`. No script or routing change was made during this analysis.
