# TLS / reverse proxy guide

Status: D2 documentation (2026-11-19). This documents supported HTTPS options and
records sandbox-only validation. It does **not** change production access. Any prod
cutover requires a separate decision covering domain, DNS, proxy placement, and a
maintenance window.

## Supported shape

Curatarr remains an HTTP application behind a TLS-terminating proxy:

- Browser → HTTPS → reverse proxy / Tailscale
- Reverse proxy → HTTP → `curatarr-web:80`
- `curatarr-web` nginx → HTTP → `curatarr-api:5000`

Do not enable TLS in ASP.NET Core or nginx inside Curatarr. The web container's nginx
already proxies `/api/` and `/hubs/` (SignalR) to the service named `curatarr-api`, so
the API service name must remain `curatarr-api` in any released Compose file.

Set `FRONTEND_BASE_URL` to the public HTTPS URL so invite/reset links are reachable.

## Option 1 — Tailscale HTTPS (recommended for a private tailnet)

This is the simplest supported path when all users are on your Tailscale network.
The existing `curatarr-tailscale-data/serve.json` already maps Tailscale HTTPS on
`:443` to the local web service on `http://127.0.0.1:80`, with `AllowFunnel` enabled.

Typical access URL:

```text
https://curatarr.<your-tailnet-name>.ts.net
```

If MagicDNS certificates are configured on the tailnet, `https://curatarr` may also
resolve. Plain `http://curatarr` should be treated as legacy wording.

Set in `.env`:

```bash
FRONTEND_BASE_URL=https://curatarr.<your-tailnet-name>.ts.net
```

Tailscale owns certificate issuance and renewal. Curatarr continues to run HTTP
behind the sidecar.

To temporarily disable public funnel access, change `AllowFunnel` to `false` in
`curatarr-tailscale-data/serve.json` and restart the Tailscale sidecar. `AllowFunnel`
being `true` means the tailnet app can be reachable publicly through Tailscale
Funnel; keep it `false` unless public access is intended.

## Option 2 — Caddy with Let's Encrypt (public hostname)

Caddy is the recommended general reverse proxy because it obtains and renews
Let's Encrypt certificates automatically.

Create `Caddyfile` next to `docker-compose.release.yml`:

```caddyfile
{$CURATARR_DOMAIN} {
    encode zstd gzip
    header {
        X-Content-Type-Options nosniff
        Referrer-Policy strict-origin-when-cross-origin
    }
    reverse_proxy curatarr-web:80
}
```

Add a Caddy service to your Compose file:

```yaml
services:
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    depends_on:
      - curatarr-web
    ports:
      - "80:80"
      - "443:443"
    environment:
      CURATARR_DOMAIN: ${CURATARR_DOMAIN}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
      - caddy-config:/config

volumes:
  caddy-data:
  caddy-config:
```

Set:

```bash
CURATARR_DOMAIN=media.example.com
FRONTEND_BASE_URL=https://media.example.com
```

DNS for `media.example.com` must point to the host running Caddy, and ports 80/443
must be reachable from the internet for HTTP-01 validation. Caddy stores certificates
in the `caddy-data` volume.

Only add HSTS headers after you are certain the hostname should be HTTPS-only for
the long term.

## Option 3 — nginx + Certbot

nginx works, but requires an explicit certificate renewal job. The essential
server block is:

```nginx
server {
    listen 443 ssl http2;
    server_name media.example.com;

    ssl_certificate     /etc/letsencrypt/live/media.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/media.example.com/privkey.pem;

    location / {
        proxy_pass http://curatarr-web:80;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

Use Certbot with the nginx plugin or a webroot challenge, and add a renewal timer.
Do not proxy directly to `curatarr-api`; that bypasses the existing web routing.

Traefik is equivalent: route HTTPS at the edge to `curatarr-web:80` and preserve
WebSocket upgrade headers for `/hubs/`.

## Sandbox-only validation

D2 was validated without modifying production:

1. Generate a short-lived self-signed certificate for `localhost`.
2. Run a scratch Caddy container on a non-production HTTPS port such as `18443`.
3. Proxy that listener to the sandbox web UI at `http://127.0.0.1:5174`.
4. Confirm the page loads over HTTPS, `/api/bootstrapped` succeeds through the proxy,
   `/hubs/` is not blocked by the proxy, and the browser console has no errors.
5. Remove the scratch proxy and certificate files.

This proves the proxy shape without changing prod compose, prod ports, cookies, or
Tailscale configuration.

## Production cutover checklist (not performed in D2)

- Choose Tailscale HTTPS or a public reverse proxy.
- Set DNS and `FRONTEND_BASE_URL`.
- Confirm certificates renew.
- Confirm `/api/`, `/hubs/`, and SignalR WebSocket upgrades work through the proxy.
- Confirm invite/reset links use the HTTPS URL.
- Decide whether `AllowFunnel` should remain enabled.
- Apply during an approved maintenance window and record the result in `PROGRESS.md`.
