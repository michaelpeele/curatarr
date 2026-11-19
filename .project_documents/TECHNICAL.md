# Curatarr — Technical Documentation

## Repo Layout (`Curatarr/`)

```
Curatarr.Api/          ASP.NET Core API (all endpoints + docker infra)
Curatarr.Engine/       Domain logic (scanner, metadata, symlink engine, scheduler) + EF models
Curatarr.Web/          React/Vite/TS frontend + nginx config + docker infra
Curatarr.EngineDemo/   (from original project — demo harness)
doctor/                Optional AI/automation agent container (compose profile)
media/                 Example media mount (empty)
output/                Example symlink-tree mount (empty)
archive/               Prior Docker infra (2026-09-06 timestamped)
docker-compose.yml     Service orchestration
.env.example           Env template (copy to .env)
README.md              User-facing quick start
```

## API Endpoints (`Curatarr.Api/Program.cs` — 35 KB single file)

Program.cs contains ALL endpoints inline (minimal APIs). New endpoint classes live in
separate partial files (extension methods) and are called from Program.cs.

### Auth / Account
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/register` | Register with email + password |
| POST | `/api/login` | Login → sets auth cookie |
| POST | `/api/logout` | Clear auth cookie |
| GET | `/api/me` | Current user info |
| PUT | `/api/me` | Update display name / email |
| PUT | `/api/me/password` | Change own password |
| POST | `/api/forgot-password` | Email reset link (console-logged) |
| POST | `/api/reset-password` | Complete password reset |
| POST | `/api/invite` | Send invite to new user |
| POST | `/api/accept-invite` | Complete invite acceptance |
| POST | `/api/confirm-email-change` | Confirm email change |
| POST | `/api/email-change` | Request email change |

### Users (Admin)
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/users` | List users (includes `roles: string[]`) |
| PUT | `/api/users/{id}` | Update user |
| DELETE | `/api/users/{id}` | Delete user |
| PUT | `/api/users/{id}/password` | Admin-set password |
| PUT | `/api/users/{id}/roles` | Replace global roles with `{ roles: ["Admin" \| "Viewer"] }` (blocks self-demotion and removing the last Admin) |

### Metadata match tooling (Admin)
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/metadata/search?query=&year=&mediaType=` | `{ available, results }` TMDB candidates; `available:false` without an API key |
| POST | `/api/metadata/match` | Apply a candidate to a row (`sourceItemId`) or a whole show (`representativeId`) |

### Library grid
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/library-entries` | Paged entries; `unmatched=true` shows rows with no TMDB id |
| GET | `/api/library-facets` | Facet options for the current (optionally unmatched) subset |

### Source Roots / Items
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/source-roots` | List roots |
| POST | `/api/source-roots` | Create root |
| DELETE | `/api/source-roots/{id}` | Delete root |
| GET | `/api/source-items` | List items (filter by root/library) |
| POST | `/api/source-items` | Create item (manual) |
| PUT | `/api/source-items/{id}` | Update item |
| DELETE | `/api/source-items/{id}` | Delete item |

### Virtual Libraries
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/virtual-libraries` | List libraries |
| POST | `/api/virtual-libraries` | Create library |
| GET | `/api/virtual-libraries/{id}` | Library detail + items |
| PUT | `/api/virtual-libraries/{id}` | Rename / update |
| DELETE | `/api/virtual-libraries/{id}` | Delete library |
| POST | `/api/virtual-libraries/{id}/members` | Add member |
| DELETE | `/api/virtual-libraries/{id}/members/{userId}` | Remove member |
| POST | `/api/virtual-libraries/{id}/approve` | Approve item (idempotent — skip if already in library) |
| POST | `/api/virtual-libraries/{id}/unapprove` | Unapprove item |
| GET | `/api/virtual-libraries/{id}/suggestions` | Suggested items |
| POST | `/api/virtual-libraries/{id}/reconcile` | Rebuild symlink tree |

### Folder-Level Symlink Engine

### Configurable Library Schedules
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/virtual-libraries/{id}/schedules` | List all configurable schedule rules for a library |
| POST | `/api/virtual-libraries/{id}/schedules` | Create a new custom schedule rule (TimeOfDay, DayOfWeekAndTime, SeasonalRange) |
| GET | `/api/virtual-libraries/{id}/schedules/{scheduleId}` | Get details of a single schedule rule |
| PUT | `/api/virtual-libraries/{id}/schedules/{scheduleId}` | Update an existing schedule rule |
| DELETE | `/api/virtual-libraries/{id}/schedules/{scheduleId}` | Delete a schedule rule |
| GET | `/api/virtual-libraries/{id}/schedules/status` | Current active status and next scheduled transition time |
| POST | `/api/virtual-libraries/{id}/schedules/evaluate` | Trigger immediate schedule evaluation & symlink synchronization |

### Path Mappings
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/path-mappings` | List mappings |
| POST | `/api/path-mappings` | Create mapping |
| DELETE | `/api/path-mappings/{id}` | Delete mapping |

### Scan / Reconcile Control
| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/scan` | Trigger full scan (SignalR progress) |
| POST | `/api/reconcile` | Trigger reconcile (SignalR progress) |
| GET | `/api/scan/status` | Current scan status |
| GET | `/api/reconcile/status` | Current reconcile status |

### Bootstrap (First-Boot Wizard)
| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/bootstrapped` | `{ isBootstrapped: bool }` — true if any user exists |
| POST | `/api/bootstrap` | One-shot wizard: creates admin + roots + mappings + library |

**Bootstrap one-shot guard:** returns 400 if `db.Users.AnyAsync()` is true. Creates
ApplicationUser, adds to Admin role, creates SourceRoots/PathMappings/VirtualLibrary +
Owner membership in a single transaction.

### SignalR
| Hub | Path | Purpose |
|-----|------|---------|
| ScanHub | `/hubs/scan` | Live progress for scan/reconcile jobs |

## Data / EF Core

- **Context:** `CuratarrDbContext` (SQLite)
- **DB path:** `ContentRootPath/curatarr.db` → `/app/data/curatarr.db` in Docker
- **Schema creation:** `SeedAsync` + `Database.MigrateAsync()` at startup. Migrations live in
  `Curatarr.Engine/Data/Migrations` (initial baseline `20261115123142_InitialCreate`). Databases
  created before migrations are auto-baselined: the startup helper creates
  `__EFMigrationsHistory` and marks `InitialCreate` applied when `SourceItems` already exists
  (no table replay, no downtime). Add new schema changes with
  `dotnet ef migrations add <Name> --project Curatarr.Engine --startup-project Curatarr.Api`.
- **Entities & Schemas:**
  - `ApplicationUser`, `SourceRoot`, `SourceItem`, `PathMapping`, `VirtualLibrary`, `VirtualLibraryItem`, `VirtualLibraryMembership`
  - `LibrarySchedule`:
    ```csharp
    public enum ScheduleType
    {
        TimeOfDay = 0,         // User selects start/stop times; applies to all days or selected days of week
        DayOfWeekAndTime = 1,  // User selects days of week combined with custom start/stop times
        SeasonalRange = 2      // User selects start/end calendar dates (with optional time-of-day & day-of-week)
    }

    public class LibrarySchedule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string VirtualLibraryId { get; set; } = default!;
        public string Name { get; set; } = default!; // e.g. "Morning Cartoons", "Weekend Prime", "Oct Spooky"
        public ScheduleType Type { get; set; }
        
        // Days of week (Bitmask: Mon=1, Tue=2, Wed=4, Thu=8, Fri=16, Sat=32, Sun=64, All=127)
        public int DaysOfWeekMask { get; set; } = 127;
        
        // Custom Start and Stop times (user selected)
        public TimeSpan? StartTime { get; set; } // e.g. 06:00:00
        public TimeSpan? StopTime { get; set; }  // e.g. 11:30:00 (supports overnight wrap like 22:00 -> 02:00)
        
        // Seasonal calendar date ranges (user selected)
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsRecurringYearly { get; set; } = true;
        
        // Seasonal optional sub-filters
        public bool IncludeTimeOfDayInSeasonal { get; set; } = false;
        public bool IncludeDaysOfWeekInSeasonal { get; set; } = false;
        
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public VirtualLibrary VirtualLibrary { get; set; } = default!;
    }
    ```
- **Unique indexes in the model:** `IX_SymlinkRecords_UniqueLinkPerLibrary` and
  `IX_VirtualLibraryItems_OnePerLibraryAndItem` are declared in `OnModelCreating` (same names
  as the original raw-SQL patches) so migration-built databases match production.
- **Logging:** console logs use ISO 8601 timestamps with UTC offset
  (`2026-11-15T12:33:30.918+00:00`); startup diagnostics go through `LogStartup` with the same
  shape. Multi-line entries (dev-mode emails, EF SQL) only carry the prefix on their first line.

## Docker Configuration

### Compose services
| Service | Image/Build | Ports | Purpose |
|---------|-------------|-------|---------|
| `curatarr-api` | Build from `Curatarr.Api/Dockerfile` | `5000:5000` (optional) | ASP.NET API |
| `curatarr-web` | Build from `Curatarr.Web/Dockerfile` | `5173:80` | nginx + SPA |
| `doctor` | Build from `doctor/Dockerfile` (profile: `doctor`) | none | AI/automation agent |

### Env vars (`.env.example`)
| Var | Default | Purpose |
|-----|---------|---------|
| `TMDB_KEY` | *(empty)* | TMDB API key — empty = folder-name parsing only |
| `FRONTEND_BASE_URL` | `https://curatarr.tail0307d.ts.net` | Base URL for invite/reset links — set to the Tailscale Funnel URL so links work off-device (compose fallback: `http://localhost:5173`) |
| `MEDIA_ROOT` | `./media` | Host media folder → mounted ro at `/media` |
| `OUTPUT_ROOT` | `./output` | Host symlink output → mounted at `/output` |
| `WEB_PORT` | `5173` | Host port for web |
| `API_PORT` | `5000` | Host port for API |
| `CODEBASE_ROOT` | `./` | Doctor: host path mounted at `/codebase:rw` |
| `DOCKER_GID` | `0` | Doctor: docker socket group ID |

### Key environment wiring
- `ASPNETCORE_CONTENTROOT=/app/data` (API Dockerfile) → DB lands in named volume at `/app/data`
- `Tmdb__ApiKey` ← `TMDB_KEY` (double-underscore = nested config key)
- `Frontend__BaseUrl` ← `FRONTEND_BASE_URL`

## Frontend Architecture

- **Entry:** `src/main.tsx` → `src/App.tsx`
- **API client:** `src/api/client.ts` — single wrapper for all endpoints (relative `/api/*`)
- **Auth:** cookie-based (`curatarr.auth`, 30-day sliding, persistent; `Cookie__Name` overrides,
  sandbox uses `curatarr.sandbox.auth`); Data Protection keys persist at
  `/app/data/data-protection-keys` so container recreates don't invalidate cookies;
  `api.me()` validates the session on boot, and a 401 from any non-auth call drops the app to
  the login screen with "Your session expired. Please sign in again."
- **Wizard gate:** `App.tsx` calls `api.isBootstrapped()` on mount; renders `<FirstBootWizard>`
  if not bootstrapped, else login/main app
- **Theme:** CSS custom properties in `src/theme/tokens.css`
  - Real tokens: `--surface-page`, `--surface-card`, `--surface-raised`, `--border`,
    `--radius-card`, `--font-heading`, `--text-on-accent`
- **View keys** (sidebar `active` prop): `library`, `calendar`, `account`, `sources`,
  `libraries`, `users`, `settings-pathmappings`, `settings-schedules`, `settings-activity`, `settings-logs`

### Responsive Layouts & Breakpoints
- **Mobile (`320px – 576px`):**
  - Bottom navigation bar (`BottomBar.tsx`) with 44×44px minimum touch targets.
  - Poster grid renders **1 to 2 columns** with responsive aspect ratios avoiding clipping.
  - Modals and schedule configuration forms render full-width/bottom sheet.
- **Tablet / iPad (`768px – 1024px`):**
  - Responsive compact sidebar/drawer adapting to iPad portrait and landscape orientations.
  - Poster grid dynamically renders **2 to 4 columns**.
  - Centered dialogs with multi-column schedule rule editors and touch spacing.
- **Desktop (`> 1024px`):**
  - Fixed sidebar, multi-column tables, **5+ column** poster grid.

### Components (`src/components/`)
- `FirstBootWizard.tsx` — 4-step guided setup (admin → roots → mappings → library)
- `LoginScreen` — in App.tsx (not separate file)
- `Sidebar.tsx` / `BottomBar.tsx` — responsive navigation shell
- `LibrariesPage.tsx` — virtual library browse/grid
- `SourcesPage.tsx` — source root management
- `UsersPage.tsx` — user/invite management (admin)
- `PathMappingsSection.tsx` — path mapping editor
- `LibrarySchedulesSection.tsx` — fully configurable schedule rule builder (custom start/stop times, day-of-week pills, seasonal date ranges)
- `FolderBrowserModal.tsx` — filesystem picker
- `DetailModal.tsx` — item detail/edit; accepts an optional `summary: ShowGroup` to show
  episode count, season count, and latest `SxxEyy` for a show
- `ScanProgressPanel.tsx` — live SignalR scan progress
- `AccountPage.tsx`, `AcceptInvitePage.tsx`, `ResetPasswordPage.tsx`, `ConfirmEmailChangePage.tsx`
- `PosterGrid.tsx` — poster grid with letter index, genre filter, and show grouping; renders
  `PosterCard` for movies and `ShowCard` for shows (single-row approve/unapprove + per-card refresh)
- `PathInput.tsx`, `DeleteXButton.tsx`, `ComingSoonPage.tsx`

### Show Grouping (`src/lib/showGroups.ts`)

- `buildShowGroups(items)` skips movies and folds episode rows into `ShowGroup`:
  - key = `${mediaType}:tmdb:${tmdbId}` when TMDB is available, else
    `${mediaType}:title:${normalizedShowTitle}`, else `${mediaType}:id:${id}`
  - `episodeCount` counts unique `SxxEyy` (duplicate copies across roots count once)
  - `representativeId(group)` returns the first row with a poster (best metadata),
    falling back to the first row — the single VLI row used for approve/unapprove since
    SymlinkEngine now creates one folder symlink per show regardless of episode count
  - `seasonCount` counts distinct season numbers; `latestSeason`/`latestEpisode` come from
    the highest parsed episode (specials / season 0 excluded from seasonCount)
  - poster/overview taken from the first row that has one
- Show and Anime stay separate even for identical titles (media type is part of the key)

## Engine Modules (`Curatarr.Engine/`)

| File | Responsibility |
|------|----------------|
| `LibraryScanner.cs` | Recursive folder scan, media detection (videos), TMDB enrichment |
| `MetadataProvider.cs` | TMDB API client (movie/show/anime lookup, poster URLs) |
| `SymlinkEngine.cs` | Builds, reconciles, and cleans symlink trees for virtual libraries w/ path mapping |
| `ScheduleEngine.cs` | Evaluates active schedule rules and dynamically manages symlink creation/pruning |

## Scan, Reconcile & Schedule Pipeline

1. **Scan:** `POST /api/scan` → `ScanControlService` walks all SourceRoots, discovers
   SourceItems, enriches w/ TMDB if key present → SignalR progress events
2. **Reconcile:** `POST /api/virtual-libraries/{id}/reconcile` → `SymlinkEngine` builds
   symlink tree in the virtual library's OutputPath, creating hard-link-free symlinks
   that point through path mappings (FromPrefix → ToPrefix)
3. **Dynamic Schedule Worker (`LibraryScheduleService.cs`):**
   - Background hosted service runs every 60 seconds (configurable).
   - Evaluates each virtual library against its configured rules:
     - **Default (No rules created):** Library is Always On (24/7/365). All symlinks remain active.
     - **Inactive → Active transition:** Trigger symlink creation in `/output` via `SymlinkEngine`.
     - **Active → Inactive transition:** Safely prune symlinks from `/output` so downstream media players only display currently scheduled content.
   - `/media` is mounted read-only and is never modified.
