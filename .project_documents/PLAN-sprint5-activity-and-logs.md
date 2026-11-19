# Sprint 5 #1 + #2 — Activity and Logs (implementation plan)

**Status:** **implemented, verified and promoted 2026-11-17.** Approved by the user and shipped
sandbox-first; 129/129 tests green, prod serves `assets/index-BoiMd5Cp.js`.

This is the shared batch for Sprint 5 **#1 Wiring up Activity** and **#2 Wiring up Logs**. Both are live-view wiring under Settings, both are Admin-only, and both need a backfill endpoint plus a SignalR push channel. The canonical workflow in `/home/michaelpeele/ai-projects/workflow.md` applies: build once, verify in sandbox first, then promote.

---

## 1. Current state

### Activity
- Sidebar entry exists but is marked `comingSoon: true` in `Curatarr.Web/src/components/Sidebar.tsx`.
- `App.tsx` renders `ComingSoonPage` for `settings-activity`.
- There is no persisted activity entity, endpoint, or hub. Events today exist only as scattered `ILogger` calls.

### Logs
- Sidebar entry exists but is marked `comingSoon: true`; `App.tsx` renders `ComingSoonPage` for `settings-logs`.
- `Curatarr.Api/Program.cs` calls `builder.Logging.ClearProviders()` then `AddSimpleConsole` with timestamp format `yyyy-MM-ddTHH:mm:ss.fffzzz `. No file sink, no ring buffer, no level endpoint.
- No log config file exists. `schedules-config.json` in the content root is the pattern to follow.
- `Curatarr.Api.csproj` has no Serilog packages.
- `docker-compose.yml` mounts `app-data` at `/app/data`; `ASPNETCORE_CONTENTROOT` is `/app/data`, so `logs-config.json` and `logs/` belong there and survive container recreation.

### Existing live-stream pattern
- `ScanHub` (`Curatarr.Api/Hubs/ScanHub.cs`) is an empty `[Authorize]` push-only hub.
- `POST /api/scan` broadcasts `ScanProgress` via `IHubContext<ScanHub>`; `ScanProgressPanel.tsx` subscribes with `@microsoft/signalr`.
- `ScanProgressEvent` already carries `Completed`, `Total`, `Title`, `Status`; `ScanControlService` tracks pause/stop but not start time or aggregate progress.

### Hook points that need activity records
| Area | Current locations |
|---|---|
| Scheduling | `ScheduleEndpoints.cs` (create/update/delete); `LibraryScheduleService.EvaluateAsync` (active/inactive flips) |
| User | `Program.cs` login, accept-invite, forgot/reset password, `/api/users` invite/role/password/delete; membership add/update/delete |
| Plex | `PlexEndpoints.cs` OAuth/link/unlink/settings; `PlexService.RecordStatusAsync` refresh result |
| Scan | `Program.cs` `/api/scan`, `/pause`, `/resume`, `/stop` |
| Source | `Program.cs` `POST/PUT/DELETE /api/source-roots` |
| Path | `Program.cs` path-mapping add/delete, library output-path change in `PUT /api/virtual-libraries/{id}`, `POST /api/reconcile` report |

---

## 2. Shared backend building blocks

### Activity persistence
Add to `Curatarr.Engine/Data/Models.cs` + `CuratarrDbContext` + a new EF migration:

```text
ActivityEvent
  Id             string   (GUID, PK)
  OccurredAt     DateTimeOffset (UTC)
  Category       enum ActivityCategory { Scheduling, User, Plex, Scan, Source, Path }
  Type           string   (stable machine name, e.g. "ScanFinished")
  Actor          string   (user email, or "Scheduler" / "Plex" / "System")
  Detail         string   (human-readable, already redacted)
  Metadata       string?  (optional compact JSON: counts, ids, status)
```

Index `(OccurredAt, Id)` descending for cursor paging. Add cascade-free relationship (activity is an audit log; deleting a library/user must not delete history).

### Activity recorder + hub
Add `Curatarr.Api/ActivityRecorder.cs` and `ActivityIngestService.cs`:

- `IActivityRecorder.Record(ActivityCategory, string type, string actor, string detail, string? metadata = null)` enqueues into a bounded `Channel<ActivityEvent>` (capacity ~5,000, `FullMode.Wait` with a short timeout so UI actions never hang).
- `ActivityIngestService : BackgroundService` drains the channel, saves in small batches (~50 / 250 ms), then broadcasts `ActivityCreated` on a new `[Authorize(Roles = "Admin")]` hub `ActivityHub` at `/hubs/activity`.
- Resolve `Actor` from `ClaimsPrincipal.Identity.Name`; background services pass explicit `"Scheduler"`, `"Plex"`, or `"System"`.
- **Never** persist tokens, passwords, invite/reset links, or full request bodies. Truncate `Detail` to ~400 chars like `PlexService.RecordStatusAsync` does for status.
- The ingest service must not log on every successful write (feedback loop). Throttled warning only on failure.

### Scan status for the countdown
Extend `ScanControlService` with `StartedAt`, `Completed`, `Total`, `Paused`, and `LastProgressAt`; set them in `/api/scan` and from the existing `onProgress` callback. Add:

- `GET /api/activity/scan-status` → `{ running, startedAt, completed, total, paused, elapsedSeconds, etaSeconds }`.

`etaSeconds` is derived from `elapsed / completed * (total - completed)` (null until at least one item completes). This is live-only while the page is open; no separate timer table.

### Activity endpoints (Admin-only)
- `GET /api/activity?limit=50&before=<cursor>&category=<Category>&type=<string>` → `{ items, nextCursor }`, newest first. Cursor is an opaque base64url of `OccurredAt|Id` so equal timestamps page deterministically.
- `GET /api/activity/scan-status` (above).

---

## 3. Logs backend

### Packages
Add to `Curatarr.Api.csproj` (versions compatible with .NET 10 / current Serilog line):
- `Serilog.AspNetCore`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

### Config
New `Curatarr.Api/LoggingConfig.cs` + `LogsConfigService.cs`:

```json
{ "level": "Information", "retentionDays": 7, "fileSizeLimitMB": 10, "archiveEnabled": true }
```

- Read `logs-config.json` from `builder.Environment.ContentRootPath` before configuring Serilog; fall back to the defaults above if missing/malformed (log a startup diagnostic, don't crash).
- Validate on write: level ∈ Trace/Debug/Information/Warning/Error/Critical; retention ∈ `7, 14, 21, 30, 60, 90`; file size 1–100 MB; write atomically (temp file + move).
- Register a singleton `LoggingLevelSwitch` initialised from the config.

### Serilog pipeline
Replace `ClearProviders()/AddSimpleConsole` with `builder.Host.UseSerilog(...)`:
- Console sink with output template preserving the current timestamp shape: `{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`.
- File sink at `{ContentRoot}/logs/curatarr-.log`, `rollingInterval: Day`, `fileSizeLimitBytes: fileSizeLimitMB * MB`, `rollOnFileSizeLimit: true`, `retainedFileTimeLimit: TimeSpan.FromDays(retentionDays)`,UTF-8.
- In-memory bounded sink `LogBufferSink : ILogEventSink` (`Curatarr.Api/LogBufferSink.cs`): `ConcurrentQueue<LogEntry>` capped at 2,000 with monotonically increasing `Seq`; render message + exception to strings. Register as a singleton and also feed a `LogBroadcaster` that publishes to `/hubs/logs` after `app.Build()` (hub context is not available during early Serilog setup, so attach the broadcaster in `Program.cs` after build).
- Avoid recursive logging: the log endpoints/hub must not emit a log entry on every read.

### Retention / archive
`LogArchiveService : BackgroundService` (`Curatarr.Api/LogArchiveService.cs`):
- On startup + hourly, zip closed daily files (`curatarr-yyyy-MM-dd*.log`, excluding today) into `{ContentRoot}/logs/archive/<date>.zip` using `System.IO.Compression`.
- Delete archives and raw rotated logs older than `retentionDays` (Serilog's `retainedFileTimeLimit` is the first line of defence; this handles zip cleanup).
- Put date/expiry decisions in pure helpers (`LogRetention.ArchiveCandidates`, `IsExpired`) so they can be unit-tested without touching disk.

### Log endpoints + hub (Admin-only)
- `GET /api/logs?after=<seq>&limit=200` → `{ entries, nextSeq }` from the ring buffer.
- `GET /api/logs/config` → current config.
- `PUT /api/logs/level` body `{ level }` → updates the `LoggingLevelSwitch` **immediately** and persists it.
- `PUT /api/logs/config` body `{ retentionDays, fileSizeLimitMB, archiveEnabled }` → persists, restarts archive decisions on next tick.
- `LogsHub` at `/hubs/logs`, `[Authorize(Roles = "Admin")]`, pushes `LogEntry` events.

---

## 4. Frontend

### Wiring
- Remove `comingSoon: true` from the Activity and Logs entries in `Sidebar.tsx`.
- Replace the two `ComingSoonPage` blocks in `App.tsx` with `<ActivityPage />` and `<LogsPage />`.
- Add to `client.ts`: `ActivityEvent`, `ActivityCategory`, `ScanStatus`, `LogEntry`, `LogConfig` types; `listActivity`, `getScanStatus`, `listLogs`, `getLogConfig`, `setLogLevel`, `setLogConfig`; SignalR connection helpers mirroring `ScanProgressPanel`.

### `ActivityPage.tsx`
- Header with live/paused indicator; category filter (All / Scheduling / User / Plex / Scan / Source / Path).
- Sticky "Scan in progress" card driven by `getScanStatus` + `/hubs/activity`: shows elapsed time, `completed/total`, paused state, and estimated remaining time as a countdown. Disappears on finish/stop.
- Newest-first list: timestamp, category icon, type, actor, detail; `Load more` uses `nextCursor`.
- Subscribe to `/hubs/activity` for `ActivityCreated`; de-duplicate by `Id`; fall back to Refresh if the hub disconnects.

### `LogsPage.tsx`
- Toolbar: **Refresh**, **Monitor live** checkbox, level dropdown (Trace → Critical), retention dropdown (`7 / 14 / 21 / 30 / 60 / 90`), Save status.
- Selecting **Debug** shows an inline warning: *Debug produces a large volume of logs and may affect performance — troubleshooting only.* The warning stays visible while Debug is selected.
- Log table: timestamp, level badge, source context, message; expandable exception text. Auto-scroll to newest while Monitor live is on.
- `Refresh` calls `GET /api/logs?after=0` (bounded by the 2,000-entry buffer); `Monitor live` subscribes to `/hubs/logs` and appends `LogEntry` events, capped in the UI as well.

---

## 5. Test plan

### Unit tests (`Curatarr.Tests`, run in the .NET 10 SDK container)
1. Activity cursor encode/decode round-trip, including equal `OccurredAt` with different `Id`.
2. Activity ordering: newest first, stable tie-break by `Id`; `nextCursor` points at the last returned row.
3. Category/type filtering applied before cursor paging.
4. Recorder redaction helper: tokens/passwords/links removed; long details truncated to 400 chars.
5. Scan ETA: null before first item, sensible value after progress, null when finished; paused state reflected.
6. Log config validation: all six accepted levels, all six retention values, rejection of `0`, `5`, `91`, invalid level, invalid file size.
7. Log config persistence: read/write round-trip through a temp JSON file; malformed file falls back to defaults.
8. `LogBufferSink`: monotonic `Seq`, 2,000-entry cap (oldest dropped), exception rendering.
9. `LogRetention.ArchiveCandidates`: only closed days, excludes today; `IsExpired` boundary exactly at cutoff.
10. Level switch update changes the running minimum level (pure `LoggingLevelSwitch` assertion).

### Integration / sandbox verification
- Viewer token gets **403** from every `/api/activity*`, `/api/logs*`, and both hubs; Admin gets 200.
- Trigger each activity category in sandbox and confirm a row appears live on the page: login; create/delete a schedule; invite a user; link/unlink Plex; start + stop a scan; add/edit/delete a source root; add/delete a path mapping; run a reconcile. Check actor, type and detail text.
- Start a scan, open Activity: countdown card shows elapsed/completed/total and decreases; stop it and the card becomes a finished/stopped row.
- Logs: change level to Debug → warning appears → `GET /api/logs/config` shows Debug → change back to Information. Change retention to 30 → restart sandbox API → config still 30 and new log files are created under `/app/data/logs`.
- Confirm no Plex token, invite link, or password appears in Activity rows or the log buffer.
- No console errors in either page; no prod changes until `--prod-only`.

---

## 6. Risks / decisions

- **Audit volume:** login and scan events are bounded by the 5,000-event channel and the 2,000-entry log ring; the activity table itself is unbounded but one row per user action is acceptable for a self-hosted family app. Add pruning in a later sprint if needed.
- **SignalR bootstrap:** the log sink is created before DI, so hub broadcasting is attached after `app.Build()`; REST polling remains a working fallback.
- **Config location:** `logs-config.json` and `logs/` live in the shared content-root volume, so sandbox and prod are independent. Promotion does not copy config; prod starts at the safe defaults (Information / 7 days).
- **Serilog versions:** verify package versions against .NET 10 during implementation; if `retainedFileTimeLimit` is unavailable, rely on `LogArchiveService` for both raw and zip retention.
- **Debug cost:** explicitly warned in the UI; default stays Information.
- **No schema coupling:** deleting a library or user must not cascade-delete audit rows; keep `ActivityEvent` standalone.

## 7. Acceptance criteria

- Both Settings pages are live, Admin-only, and no longer show "coming soon".
- Activity rows are persisted, newest first, paged, and stream live; scan progress has a countdown.
- Logs are written to console **and** rolling files; config persists across restarts; level changes immediately; retention is capped at 90 days with the six required options.
- All unit tests pass; `docker compose build api` and `docker compose build web` are green; sandbox verification passes before `--prod-only`.
