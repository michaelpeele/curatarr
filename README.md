# Curatarr

**A self-hosted media curation server** — ASP.NET Core 10 API + React 18 SPA + SQLite + ASP.NET Identity cookie auth.

Build curated virtual libraries of movies, shows, and anime from your media collection. Schedule when libraries become available (e.g., Saturday morning cartoons, seasonal content), manage access with user invites, and create dynamic symlink trees for Plex or Jellyfin to index.

---

## Features

- **First-Boot Wizard** — 4-step guided setup (admin account, media source roots, path mappings, initial library)
- **Virtual Libraries** — curate subsets of your media into themed libraries (e.g., "Kids Movies," "Anime," "Family Classics")
- **One Poster Per Show** — episodes are grouped into a single show card with episode/season counts and latest episode; "Mark for library" approves the whole show in one action
- **User Management** — invite users with role-based access (Owner, Manager, Viewer) per library
- **Media Scanning** — folder-name parsing + TMDB metadata enrichment (posters, release dates, genres)
- **Dynamic Symlinks** — creates virtual library folder trees without duplicating or moving source media files
- **Path Mapping** — translate container paths to Plex/Jellyfin paths so symlinks resolve correctly
- **Scheduling (Planned)** — time-of-day, day-of-week, and seasonal content scheduling per library

---

## Quick Start

### Prerequisites

- **Docker + Docker Compose** (or Podman with compose plugin)
- **(Optional) Tailscale** — for accessing Curatarr via `http://curatarr` on your Tailnet
- **(Optional) TMDB API Key** — for poster images and metadata enrichment (get one at [themoviedb.org](https://www.themoviedb.org/settings/api))

### Released install (recommended)

If you just want to run the published V1 images, you do not need to clone this repository:

```bash
mkdir curatarr && cd curatarr
curl -O https://raw.githubusercontent.com/michaelpeele/curatarr/v1.0.0/docker-compose.release.yml
cp /path/to/your/.env .env   # or create one using the variables below
docker compose -f docker-compose.release.yml up -d
```

The release file pulls `ghcr.io/michaelpeele/curatarr-api:1.0.0` and `ghcr.io/michaelpeele/curatarr-web:1.0.0`. Set `CURATARR_RELEASE_TAG` to choose another published tag such as `1.0`, `latest`, or `sha-<short-sha>`. API and web remain two containers; the web container proxies `/api/` and `/hubs/` to the service named `curatarr-api`.

At minimum, set `HOST_MEDIA_DIR`, `HOST_OUTPUT_DIR`, `HOST_VIRTUAL_LIBS_DIR`, and `FRONTEND_BASE_URL` in `.env`. `TMDB_KEY` and SMTP variables are optional. The API database is stored in the `curatarr-data` named volume.

The source-tree install below is still the right choice for local development or custom builds.

### Setup

1. **Clone the repo:**
   ```bash
   git clone https://github.com/michaelpeele/curatarr.git
   cd curatarr
   ```

2. **Create `.env` from template:**
   ```bash
   cp .env.example .env
   ```

3. **Edit `.env`:**
   - `HOST_MEDIA_DIR` — host path to your source media (mounted read-only at `/media` in container)
   - `HOST_OUTPUT_DIR` — host path for virtual library symlink trees (mounted at `/output` in container)
   - `HOST_VIRTUAL_LIBS_DIR` — host path served by nginx for generated symlink trees
   - `TMDB_KEY` — (optional) your TMDB API key for posters/metadata
   - `TS_AUTHKEY` — (optional) Tailscale auth key to join your Tailnet automatically

4. **Start the stack:**
   ```bash
   docker compose up --build -d
   ```

5. **Open the first-boot wizard:**
   - **Local:** `http://localhost:5173`
   - **LAN:** `http://<HOST_IP>:5173`
   - **Tailscale:** `http://curatarr` (if you set `TS_AUTHKEY` in `.env`)

6. **Complete the wizard:**
   - **Step 1:** Create admin account (username, password, display name)
   - **Step 2:** Register source media roots (e.g., `/media/Movies`, `/media/TV Shows`)
   - **Step 3:** Add path mappings so symlinks point to paths Plex/Jellyfin can see
   - **Step 4:** Create your first virtual library (name, output path like `/output/KidsMovies`, media type)

---

## Tailscale Setup (Access via `http://curatarr`)

Curatarr includes a Tailscale sidecar for easy access across your devices via your private Tailnet.

### Generate Tailscale Auth Key

1. Go to [Tailscale Admin Console](https://login.tailscale.com/admin/settings/keys)
2. Click **Generate auth key**
3. Choose:
   - **Reusable:** Yes (so you can rebuild containers without generating new keys)
   - **Ephemeral:** No (device persists in your Tailnet even when offline)
   - **Expiration:** Optional (90 days default, or never)
4. Copy the key (format: `tskey-auth-kXXXXX-XXXXXXXXX`)

### Configure `.env`

Add the key to `/codebase/Curatarr/.env`:
```bash
TS_AUTHKEY=tskey-auth-kXXXXX-XXXXXXXXX
```

Optional: Add tags for ACL-based access control:
```bash
TS_EXTRA_ARGS=--advertise-tags=tag:container
```

### Restart Services

```bash
docker compose up -d
```

The `ts-curatarr` container will join your Tailnet with hostname `curatarr`. Within a few seconds, you can access Curatarr at:

**`http://curatarr`**

from any device connected to your Tailscale network (laptop, phone, tablet).

---

## Architecture

### Services

- **`curatarr-api`** — ASP.NET Core 10 API (port 5000)
  - SQLite database at `/app/data/curatarr.db` (persisted in `curatarr-data` volume)
  - Mounts `HOST_MEDIA_DIR` (read-only) and `HOST_OUTPUT_DIR` (read-write)
- **`curatarr-web`** — React 18 SPA served via nginx (port 80, mapped to host `WEB_LOCAL_PORT`)
  - Proxies `/api/*` and `/hubs/*` (SignalR) to the API container
  - Shares network namespace with `tailscale` container
- **`tailscale`** — Tailscale sidecar (optional)
  - Joins your Tailnet with hostname `curatarr`
  - Exposes `curatarr-web` on port 80 (mapped to host `WEB_LOCAL_PORT`)

### Data Model

- **`SourceRoot`** — media folder roots (e.g., `/media/Movies`, `/media/TV Shows`)
- **`SourceItem`** — individual media files/folders discovered during scan (one row per episode file for shows/anime; the UI groups them into shows)
- **`PathMapping`** — translate container paths → Plex/Jellyfin paths for symlink resolution
- **`VirtualLibrary`** — curated library (name, output path, media type, owner)
- **`VirtualLibraryMembership`** — user roles (Owner, Manager, Viewer) per library
- **`VirtualLibraryItem`** — links `SourceItem` → `VirtualLibrary` (the curation mapping)

---

## Configuration

All configuration via `.env` file (see `.env.example` for full reference):

| Variable | Default | Description |
|----------|---------|-------------|
| `APP_NAME` | `curatarr` | Container/volume name prefix |
| `APP_NAME_PASCAL` | `Curatarr` | Source folder prefix used by Dockerfiles |
| `CURATARR_VERSION` | `latest` | Tag applied to locally built API/web images |
| `CURATARR_RELEASE_TAG` | `1.0.0` | Tag used by `docker-compose.release.yml` for published GHCR images |
| `HOST_MEDIA_DIR` | `./media` | Host path to source media (read-only mount) |
| `HOST_OUTPUT_DIR` | `./output` | Host path for virtual library symlink trees |
| `HOST_VIRTUAL_LIBS_DIR` | `./output/VirtualLibraries` | Host path served by nginx for generated symlink trees |
| `WEB_LOCAL_PORT` | `5173` | Host port for web UI |
| `API_LOCAL_PORT` | `5000` | Host port for API (optional; for debugging) |
| `TMDB_KEY` | *(empty)* | TMDB API key (optional; enables posters/metadata) |
| `TS_AUTHKEY` | *(empty)* | Tailscale auth key (optional; joins Tailnet) |
| `TS_EXTRA_ARGS` | *(empty)* | Extra Tailscale flags (e.g., `--advertise-tags=tag:container`) |
| `FRONTEND_BASE_URL` | `http://localhost:5173` | Public URL for invite/reset links in emails — must be recipient-reachable (set to your Tailnet URL in production) |

### Published Images

GitHub Actions publishes multi-architecture images to GitHub Container Registry (GHCR) when a `v*` tag is pushed, a release is published, or the workflow is dispatched manually. Both images include `linux/amd64` and `linux/arm64` in a single manifest.

| Image | Pull command |
|-------|--------------|
| API | `docker pull ghcr.io/michaelpeele/curatarr-api:latest` |
| Web | `docker pull ghcr.io/michaelpeele/curatarr-web:latest` |

Published tags include semantic versions (for example `1.2.3` and `1.2`), the commit SHA (`sha-<short-sha>`), and `latest` for tag/release runs. A manual dispatch on `main` publishes the SHA tag only; use that tag when inspecting or pulling that run. Inspect the available platforms with:

```bash
docker buildx imagetools inspect ghcr.io/michaelpeele/curatarr-api:latest
docker buildx imagetools inspect ghcr.io/michaelpeele/curatarr-web:latest
```

For the recommended one-file install, use [`docker-compose.release.yml`](docker-compose.release.yml) with `docker compose -f docker-compose.release.yml up -d`; see **Released install** above. The API service name must remain `curatarr-api`.

The workflow uses the repository's built-in `GITHUB_TOKEN` with `packages: write`; no custom GitHub secret is required. Local builds and `scripts/promote.sh` still build and use `curatarr-api:latest` / `curatarr-web:latest` from this source tree.

---

## Development Mode (No Docker)

If you want to run the API and frontend directly on your host (useful for debugging with breakpoints):

### API

```bash
cd Curatarr.Api
dotnet run
# Listens on http://localhost:5000
```

### Frontend

```bash
cd Curatarr.Web
npm install
npm run dev
# Listens on http://localhost:5173
```

Update `Curatarr.Web/vite.config.ts` to proxy `/api` → `http://localhost:5000` instead of `curatarr-api:5000`.

---

## Troubleshooting

### First-Boot Wizard Not Showing

Check if database is bootstrapped:
```bash
curl http://localhost:5000/api/bootstrapped
# {"isBootstrapped":false} → wizard should appear
# {"isBootstrapped":true} → login page shown instead
```

Reset database:
```bash
docker exec curatarr-api rm -f /app/data/curatarr.db /app/data/curatarr.db-shm /app/data/curatarr.db-wal
docker restart curatarr-api
```

### Cannot Access via Tailscale Hostname

1. Verify `TS_AUTHKEY` is set in `.env`
2. Check `ts-curatarr` container logs:
   ```bash
   docker logs ts-curatarr
   ```
3. Confirm device shows in [Tailscale Admin](https://login.tailscale.com/admin/machines) with hostname `curatarr`

### API Port Already in Use

Edit `.env`:
```bash
API_LOCAL_PORT=5001
```

### Symlinks Not Working in Plex/Jellyfin

1. Check `PathMapping` configuration — translate container paths (`/media/...`) to paths Plex sees (`/mnt/data/media/...`)
2. Ensure Plex/Jellyfin has read access to `HOST_OUTPUT_DIR` and can follow symlinks
3. Verify symlinks resolve correctly:
   ```bash
   docker exec curatarr-api ls -lah /output/<YourLibrary>
   ```

---

## Security Notes

- **No HTTPS by default** — cookie auth over plain HTTP. Use a reverse proxy (Caddy, nginx, Traefik) with Let's Encrypt for production.
- **SQLite unencrypted** — volume access = full data access. Use filesystem-level encryption if needed.
- **API port exposed** — optional; remove the `API_LOCAL_PORT` mapping in `docker-compose.yml` if not needed.

---

## Roadmap

See `.project_documents/ROADMAP.md` for planned features:

- **Mobile & Tablet Responsive Views** — adaptive layouts for all device sizes
- **Library Scheduling Engine** — time-of-day, day-of-week, and seasonal content scheduling
- **TMDB Metadata Polish** — display posters/genres in UI grids
- **Calendar View** — upcoming releases filtered by library/media type
- **Activity Logs** — scan history, errors, user audit trail
- **Backup/Restore** — see [`.project_documents/BACKUP-RESTORE.md`](./.project_documents/BACKUP-RESTORE.md)

---

## License

MIT License — see `LICENSE` file for details.

---

## Contributing

Contributions welcome! Open an issue or PR on GitHub.

---

## Support

- **GitHub Issues:** [github.com/michaelpeele/curatarr/issues](https://github.com/michaelpeele/curatarr/issues)
- **Docs:** `.project_documents/` folder (DESIGN.md, TECHNICAL.md, ROADMAP.md)
