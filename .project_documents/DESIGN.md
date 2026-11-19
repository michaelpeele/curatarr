# Curatarr — Design

## Overview

Curatarr is a self-hosted media curation server. It scans media folders (movies/shows/anime),
enriches them with metadata from TMDB, and builds **virtual libraries** — symlink trees that
let you curate subsets of your media without moving the originals.

## Architecture

```
┌──────────────────────┐     ┌──────────────────────┐     ┌────────────────────┐
│   curatarr-web       │     │   curatarr-api       │     │   curatarr-data    │
│   nginx :80          ├────►│   ASP.NET :5000      ├────►│   SQLite volume    │
│   (host :5173)       │     │   Kestrel            │     │   curatarr.db      │
└──────────────────────┘     └──────────┬───────────┘     └────────────────────┘
                                        │
                              ┌─────────▼──────────┐
                              │  /media (/media:ro)│  Source media (read-only)
                              │  /output (/output) │  Symlink trees (rw)
                              └────────────────────┘
```

## Components

### API (`Curatarr.Api`)
- **Framework:** ASP.NET Core (minimal APIs), C# 12
- **Storage:** SQLite via EF Core, `curatarr.db` in `/app/data` (ContentRootPath)
- **Auth:** ASP.NET Identity, cookie-based (no JWT)
- **Real-time:** SignalR `/hubs/scan` for live scan/reconcile progress
- **Roles:** Admin / Curator / Viewer
- **Email:** `SmtpEmailSender` when `Smtp__Host` is set (Gmail app password or any standard
  SMTP relay, `System.Net.Mail` — no extra NuGet deps); otherwise `ConsoleEmailSender` logs
  links to stdout. Config keys `Smtp__Host/Port/User/Password/From` in `.env`.
- **Schedule Background Worker:** Periodic hosted service (`LibraryScheduleService`) that evaluates configurable library schedules every minute and manages symlink creation/pruning based on active windows.

### Engine (`Curatarr.Engine`)
- **`LibraryScanner.cs`** — recursive folder scan, media detection, TMDB enrichment
- **`MetadataProvider.cs`** — TMDB API client (optional key; falls back to folder-name parsing)
- **`SymlinkEngine.cs`** — builds/updates/prunes virtual-library symlink trees with path mapping
- **`ScheduleEngine.cs`** — evaluates user-configured schedule rules (custom start/stop times, day-of-week selections, and seasonal calendar ranges) to determine dynamic library visibility.

### Web (`Curatarr.Web`)
- **Framework:** React 18 + Vite 5 + TypeScript
- **Served by:** nginx (SPA static files + `/api` + `/hubs` proxy)
- **No VITE_ env vars** — all client API calls are relative (`/api/*`)
- **Theme:** CSS custom properties in `src/theme/tokens.css`
- **Responsive Views:** Dedicated layout adjustments for mobile (320px–576px), tablet/iPad (768px–1024px), and desktop.
- **Show grouping:** `src/lib/showGroups.ts` folds the per-episode `SourceItem` rows into
  `ShowGroup` objects, and `PosterGrid.tsx` renders one `ShowCard` per show. Movies remain
  one card per item. Grouping is client-side only — no show table, no migration.

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **SQLite** (not Postgres) | Single-user / small-scale self-host; zero external deps |
| **Cookie auth** (not JWT) | Simpler, matches ASP.NET Identity defaults, fine behind reverse proxy |
| **Dual-mode email** | Real SMTP (`SmtpEmailSender`) when `Smtp__Host` configured; zero-setup console fallback keeps links in `docker logs` for dev |
| **Path mapping** | Media lives on host at arbitrary paths; symlinks must point where Plex sees them |
| **Ports 5173/5000** | Match original Vite dev server + API ports → existing bookmarks work |
| **First-Boot Wizard** | Zero-input onboarding — no manual SQL or seeding required |
| **Plain HTTP** | Self-hosted on LAN; TLS delegated to reverse proxy if needed |
| **Dynamic Symlink Scheduling** | Media servers (Plex, Jellyfin) index directory contents automatically; creating and removing symlinks dynamically alters availability without modifying source files |
| **Fully Configurable Schedules** | No hardcoded presets; users configure custom start/stop times, any day-of-week combinations, and seasonal date ranges |
| **Mobile & Tablet First Layouts** | Responsive poster grids (1–2 cols mobile, 2–4 cols tablet) and touch-optimized navigation prevent clipping and provide native-like usability |
| **One poster per show** | The scanner stores one row per episode; the UI groups episode rows by TMDB id (else normalized show title + media type) so a 12-episode show is one card with episode/season/latest stats instead of 12 near-identical cards |
| **Free-text titles are the fallback** | TMDB enrichment is optional, so show grouping must also work keyless; normalized folder-derived `ShowTitle` (case/trim-insensitive) is the fallback key |
| **Single-row approval** | Approving a show approves one representative row (first with a poster, else first row); `SymlinkEngine` links the show folder flattened to the library root (letter groups dropped), so one VLI row per show is enough. All-or-nothing: no partial badge |
| **Folder-level show symlinks** | Shows/anime link the whole show dir in one symlink, **flattened to the library root** — letter-group source containers are dropped (`/output/Bluey → /media/tvshows/M thru Z/Bluey`), so Plex/Jellyfin walks the season tree natively from the library root; movies link per file unchanged |

## Data Model

- **ApplicationUser** — Identity user (UserName, Email, DisplayName)
- **SourceRoot** — top-level media folder (Name, Path, MediaType)
- **SourceItem** — individual media item under a root (RelativePath, Title, MediaType, metadata).
  For TV/anime this is **one row per episode file**; a "show" is a UI-level grouping of rows
  sharing a TMDB id or normalized `ShowTitle`, not a database entity.
- **PathMapping** — FromPrefix → ToPrefix translation for symlink targets
- **VirtualLibrary** — curated collection (Name, MediaType, OutputPath, IsActive)
- **VirtualLibraryItem** — membership of a SourceItem in a VirtualLibrary (approval status)
- **VirtualLibraryMembership** — user's role (Owner/Curator/Viewer) in a library
- **LibrarySchedule** — user-configurable scheduling rule linked to a VirtualLibrary:
  - `Id` (GUID string)
  - `VirtualLibraryId` (FK)
  - `Name` (User label, e.g., "Morning Rotation", "Weekend Evenings", "Holiday Collection")
  - `ScheduleType` (`TimeOfDay`, `DayOfWeekAndTime`, `SeasonalRange`)
  - `DaysOfWeekMask` (Bitmask flags for Mon=1, Tue=2, Wed=4, Thu=8, Fri=16, Sat=32, Sun=64, or 127 for All Days)
  - `StartTime` / `StopTime` (User-selected custom time of day, e.g. 06:00 to 11:30; supports overnight spans e.g. 22:00 to 03:00)
  - `StartDate` / `EndDate` (User-selected calendar dates for seasonal content)
  - `IsRecurringYearly` (Boolean; true for annual seasonal recurrence)
  - `IncludeTimeOfDayInSeasonal` (Boolean; whether seasonal rule also applies custom StartTime/StopTime)
  - `IncludeDaysOfWeekInSeasonal` (Boolean; whether seasonal rule also filters by specific days of week)
  - `IsEnabled` (Boolean toggle)

## Responsive View Design (Mobile & Tablet)

### 1. Mobile View (320px – 576px)
- **Navigation:** Collapsible sidebar transitions into a fixed bottom navigation bar (`BottomBar.tsx`) with 44×44px minimum touch targets.
- **Poster Grid (`PosterGrid.tsx`):** Displays **1 to 2 columns** with responsive aspect ratios, eliminating clipping and horizontal scrollbars.
- **Modals & Schedule Builder:** Full-screen dialogs / slide-up bottom sheets with touch-friendly time and date pickers.
- **Form Controls:** Full-width inputs and action buttons.

### 2. Tablet View / iPad (768px – 1024px)
- **Navigation:** Compact collapsible sidebar or responsive drawer optimized for iPad portrait and landscape orientations.
- **Poster Grid:** Fluid **2 to 4 columns** adjusting dynamically based on orientation and split-screen multitasking.
- **Modals & Management Views:** Centered dialogs with multi-column layout and touch-optimized padding.

### 3. Desktop View (> 1024px)
- Fixed vertical sidebar navigation, multi-column data tables, and **5+ column** poster grid.

## Fully Configurable Scheduling & Dynamic Symlink Engine

Curatarr provides user-configured scheduling rules to dynamically control when virtual library content is exposed to downstream media servers (Plex, Jellyfin):

### 1. Default State: "Always On"
- If a Virtual Library has **no schedules created**, its default behavior is **Always On** (24/7/365). All approved symlinks remain active in `/output`.

### 2. Schedule Types & User Configuration Options
Users can create multiple custom schedule rules per library:

1. **Schedule Type 1 — Time of Day (All Days or Selected Days):**
   - User chooses custom **Start Time** and **Stop Time** (e.g., 06:00 to 11:30 for morning content, 12:00 to 17:30 for afternoon, 18:00 to 23:00 for evening).
   - User selects day applicability: **All Days** (every day) OR **Specific Days of the Week** (Mon, Tue, Wed, Thu, Fri, Sat, Sun).
   - Supports overnight windows (e.g., 22:00 to 02:00).

2. **Schedule Type 2 — Time of Day + Day of Week Combined:**
   - User specifies custom Start/Stop time windows coupled with specific day-of-week selections (e.g., Saturday & Sunday 07:00–11:00, or Friday & Saturday 20:00–01:00).

3. **Schedule Type 3 — Time of Year / Seasonal Content:**
   - User specifies custom **Start Date** and **End Date** (e.g., Oct 1 to Oct 31, Dec 1 to Dec 31).
   - Can recur annually (`IsRecurringYearly = true`).
   - **Optional Sub-Filters:** User can optionally check:
     - *Include Time of Day:* Custom Start Time to Stop Time inside the seasonal window.
     - *Include Day of Week:* Specific days of the week inside the seasonal window.

### 3. Union Logic for Multiple Schedules
- A library can have multiple schedule rules.
- If **any** enabled schedule rule evaluates to true at the current moment, the library is considered **Active**.
- If all enabled schedule rules evaluate to false, the library is **Inactive**.

### 4. Dynamic Symlink Addition and Removal
- **Window Activation:** When a library transitions from Inactive to Active, `ScheduleEngine` reconciles the library and creates symlinks in the library's `/output` folder.
- **Window Deactivation:** When a library transitions from Active to Inactive, symlinks are removed/unlinked from the library's `/output` folder.
- **Media Safety:** Source media in `/media` is mounted read-only and is never altered or deleted.
