using System.Globalization;
using Curatarr.Engine;
using Curatarr.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

// Library schedules (Sprint 3 Slice 1). Admin-only, like the rest of the
// library configuration surface.
public static class ScheduleEndpoints
{
    public static void MapLibraryScheduleEndpoints(this WebApplication app)
    {
        // ------------------------------------------------------------------
        // Settings → Server: view/save the global schedule timezone
        // ------------------------------------------------------------------
        app.MapGet("/api/schedules/settings", (LogTimeZone logTz, IConfiguration config) =>
        {
            var configured = config["Schedules:TimeZone"]?.Trim() ?? "";
            // Echo the full zone list so the UI can render the dropdown.
            // The list is stable (system tzdata), cached in a static for
            // the lifetime of the process — no per-request I/O.
            var zones = GetTimeZones();
            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(configured) ? TimeZoneInfo.Local
                    : TryFindZone(configured));
            return Results.Ok(new
            {
                timeZone        = configured,
                isConfigured    = !string.IsNullOrWhiteSpace(configured),
                nowInZone       = nowLocal.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                zones           = zones,
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapPut("/api/schedules/settings", async (
            SetScheduleSettingsRequest req,
            LogTimeZone logTz,
            ILogger<LogTimeZone> logger) =>
        {
            if (req is null) return Results.BadRequest("Missing body");
            var zone = (req.TimeZone ?? "").Trim();

            // Validate the zone before writing anything.
            if (!string.IsNullOrWhiteSpace(zone) && !IsKnownZone(zone))
            {
                return Results.BadRequest($"Unknown or invalid timezone '{zone}' on this host.");
            }

            // Write to the app-data volume so the value survives restarts.
            // In production the volume is /app/data (ASPNETCORE_CONTENTROOT),
            // matching where the Data Protection keys live.
            var contentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT")
                ?? AppContext.BaseDirectory;
            var actualPath = Path.Combine(contentRoot, "schedules-config.json");

            await File.WriteAllTextAsync(actualPath,
                System.Text.Json.JsonSerializer.Serialize(
                    new { Schedules = new { TimeZone = zone } }));

            // Reload the LogTimeZone so new log lines immediately use the
            // new zone. The ScheduleTimeZone singleton still holds the old
            // zone until the next restart; the worker reconciles using
            // ScheduleTimeZone, so a restart notice is shown in the UI.
            LogTimeZone.Reload(zone);
            logger.LogInformation("Schedule timezone set to '{Zone}' (log timestamps now use this zone; restart API for schedule engine to pick it up).", zone);

            return Results.Ok(new
            {
                timeZone   = zone,
                message    = string.IsNullOrWhiteSpace(zone)
                    ? "Timezone cleared; server local time will be used."
                    : "Timezone saved. Restart the API container for the schedule engine to pick up the change.",
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/virtual-libraries/{libraryId}/schedules", async (
            string libraryId, CuratarrDbContext db) =>
        {
            if (!await db.VirtualLibraries.AnyAsync(v => v.Id == libraryId)) return Results.NotFound();
            var schedules = await db.LibrarySchedules
                .Where(s => s.VirtualLibraryId == libraryId)
                .OrderBy(s => s.StartTime).ThenBy(s => s.Id)
                .ToListAsync();
            return Results.Ok(schedules.Select(ToDto));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/virtual-libraries/{libraryId}/schedules/{scheduleId}", async (
            string libraryId, string scheduleId, CuratarrDbContext db) =>
        {
            var schedule = await db.LibrarySchedules
                .FirstOrDefaultAsync(s => s.Id == scheduleId && s.VirtualLibraryId == libraryId);
            return schedule is null ? Results.NotFound() : Results.Ok(ToDto(schedule));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapPost("/api/virtual-libraries/{libraryId}/schedules", async (
            string libraryId, ScheduleRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
        {
            if (!await db.VirtualLibraries.AnyAsync(v => v.Id == libraryId)) return Results.NotFound();
            if (!TryParseTimes(req.StartTime, req.EndTime, out var start, out var end, out var timeError))
                return Results.BadRequest(new { message = timeError });
            var validation = ScheduleEngine.Validate(req.DaysOfWeek, req.StartMonth, req.StartDay, req.EndMonth, req.EndDay);
            if (validation is not null) return Results.BadRequest(new { message = validation });

            var schedule = new LibrarySchedule
            {
                Id = Guid.NewGuid().ToString(),
                VirtualLibraryId = libraryId,
                Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim(),
                Enabled = req.Enabled ?? true,
                DaysOfWeek = ScheduleEngine.NormalizeDays(req.DaysOfWeek),
                StartTime = start,
                EndTime = end,
                StartMonth = req.StartMonth,
                StartDay = req.StartDay,
                EndMonth = req.EndMonth,
                EndDay = req.EndDay
            };
            db.LibrarySchedules.Add(schedule);
            await db.SaveChangesAsync();
            activity.Record(ActivityCategory.Scheduling, "ScheduleCreated", "System",
                $"Created schedule {schedule.Name ?? schedule.Id} on library {libraryId}");
            return Results.Ok(ToDto(schedule));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapPut("/api/virtual-libraries/{libraryId}/schedules/{scheduleId}", async (
            string libraryId, string scheduleId, ScheduleRequest req, CuratarrDbContext db, IActivityRecorder activity) =>
        {
            var schedule = await db.LibrarySchedules
                .FirstOrDefaultAsync(s => s.Id == scheduleId && s.VirtualLibraryId == libraryId);
            if (schedule is null) return Results.NotFound();
            if (!TryParseTimes(req.StartTime, req.EndTime, out var start, out var end, out var timeError))
                return Results.BadRequest(new { message = timeError });
            var validation = ScheduleEngine.Validate(req.DaysOfWeek, req.StartMonth, req.StartDay, req.EndMonth, req.EndDay);
            if (validation is not null) return Results.BadRequest(new { message = validation });

            schedule.Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
            schedule.Enabled = req.Enabled ?? schedule.Enabled;
            schedule.DaysOfWeek = ScheduleEngine.NormalizeDays(req.DaysOfWeek);
            schedule.StartTime = start;
            schedule.EndTime = end;
            schedule.StartMonth = req.StartMonth;
            schedule.StartDay = req.StartDay;
            schedule.EndMonth = req.EndMonth;
            schedule.EndDay = req.EndDay;
            schedule.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            activity.Record(ActivityCategory.Scheduling, "ScheduleUpdated", "System",
                $"Updated schedule {schedule.Name ?? schedule.Id} on library {libraryId}");
            return Results.Ok(ToDto(schedule));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapDelete("/api/virtual-libraries/{libraryId}/schedules/{scheduleId}", async (
            string libraryId, string scheduleId, CuratarrDbContext db, IActivityRecorder activity) =>
        {
            var schedule = await db.LibrarySchedules
                .FirstOrDefaultAsync(s => s.Id == scheduleId && s.VirtualLibraryId == libraryId);
            if (schedule is null) return Results.NotFound();
            db.LibrarySchedules.Remove(schedule);
            await db.SaveChangesAsync();
            activity.Record(ActivityCategory.Scheduling, "ScheduleDeleted", "System",
                $"Deleted schedule {schedule.Name ?? schedule.Id} on library {libraryId}");
            return Results.Ok();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Current state + next flip. `?at=<ISO instant>` evaluates at another
        // moment (great for deterministic tests); defaults to now.
        app.MapGet("/api/virtual-libraries/{libraryId}/schedules/status", async (
            string libraryId, string? at, CuratarrDbContext db, ScheduleTimeZone tz) =>
        {
            if (!await db.VirtualLibraries.AnyAsync(v => v.Id == libraryId)) return Results.NotFound();
            var schedules = await db.LibrarySchedules
                .Where(s => s.VirtualLibraryId == libraryId)
                .ToListAsync();

            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(at) && !TryParseInstant(at, out now))
                return Results.BadRequest(new { message = "at must be an ISO 8601 instant, e.g. 2026-11-15T20:00:00Z." });

            var active = ScheduleEngine.IsActive(schedules, now, tz.Zone);
            var next = ScheduleEngine.NextTransition(schedules, now, tz.Zone);
            return Results.Ok(new
            {
                libraryId,
                isActive = active,
                alwaysOn = ScheduleEngine.IsAlwaysOn(schedules),
                timeZone = tz.Id,
                evaluatedAt = now.ToUniversalTime(),
                nextTransitionAt = next?.ToUniversalTime(),
                nextIsActivation = next is null ? (bool?)null : !active,
                scheduleCount = schedules.Count,
                enabledCount = schedules.Count(s => s.Enabled)
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Apply the schedule right now instead of waiting for the next worker
        // tick. Returns the reconcile report plus the resulting state.
        app.MapPost("/api/virtual-libraries/{libraryId}/schedules/evaluate", async (
            string libraryId, CuratarrDbContext db, ScheduleTimeZone tz, SymlinkEngine engine, PlexService plex,
            IActivityRecorder activity) =>
        {
            if (!await db.VirtualLibraries.AnyAsync(v => v.Id == libraryId)) return Results.NotFound();
            var schedules = await db.LibrarySchedules
                .Where(s => s.VirtualLibraryId == libraryId)
                .ToListAsync();

            var active = ScheduleEngine.IsActive(schedules, DateTimeOffset.UtcNow, tz.Zone);
            var report = await engine.ReconcileLibraryAsync(libraryId, active);
            plex.TriggerInBackground([libraryId]);
            activity.Record(ActivityCategory.Scheduling, "ScheduleEvaluated", "System",
                $"Evaluated schedules for library {libraryId} (active={active})");
            return Results.Ok(new
            {
                libraryId,
                isActive = active,
                timeZone = tz.Id,
                created = report.Created,
                removed = report.Removed,
                broken = report.Broken,
                unchanged = report.Unchanged
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static object ToDto(LibrarySchedule s) => new
    {
        s.Id,
        s.VirtualLibraryId,
        s.Name,
        s.Enabled,
        daysOfWeek = (s.DaysOfWeek ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var d) ? (int?)d : null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToArray(),
        startTime = s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        endTime = s.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.StartMonth,
        s.StartDay,
        s.EndMonth,
        s.EndDay,
        s.CreatedAt,
        s.UpdatedAt
    };

    private static bool TryParseTimes(
        string? startText, string? endText,
        out TimeOnly start, out TimeOnly end, out string? error)
    {
        start = default;
        end = default;
        error = null;

        if (!TryParseTime(startText, out start) || !TryParseTime(endText, out end))
        {
            error = "Start and end times must look like \"HH:mm\" (24-hour).";
            return false;
        }
        return true;
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return TimeOnly.TryParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
            || TimeOnly.TryParseExact(value.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
            || TimeOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static bool TryParseInstant(string value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant);

    // ── Timezone helpers ──────────────────────────────────────────────────
    // Cached on first call; tzdata is stable for the lifetime of the container.

    private static string[]? _cachedZones;

    private static string[] GetTimeZones()
    {
        if (_cachedZones is not null) return _cachedZones;
        var list = new List<string>();
        try
        {
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                if (zone.Id is "Standard" or "Daylight" or "Generic") continue;
                if (!zone.Id.StartsWith("Etc/UTC", StringComparison.Ordinal))
                    list.Add(zone.Id);
            }
            list.Sort(StringComparer.Ordinal);
        }
        catch (PlatformNotSupportedException)
        {
            list.AddRange(new[]
            {
                "UTC", "America/New_York", "America/Chicago", "America/Denver",
                "America/Los_Angeles", "Europe/London", "Europe/Berlin",
                "Asia/Tokyo", "Australia/Sydney",
            });
        }
        _cachedZones = [.. list];
        return _cachedZones;
    }

    private static bool IsKnownZone(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch { return false; }
    }

    private static TimeZoneInfo TryFindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        catch { return TimeZoneInfo.Local; }
    }
}

public record ScheduleRequest(
    string? Name,
    bool? Enabled,
    int[]? DaysOfWeek,
    string? StartTime,
    string? EndTime,
    int? StartMonth,
    int? StartDay,
    int? EndMonth,
    int? EndDay);

public record SetScheduleSettingsRequest(string? TimeZone);
