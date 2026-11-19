using Serilog.Core;

namespace Curatarr.Api;

public sealed record SetLogLevelRequest(string Level);
public sealed record SetLogConfigRequest(int RetentionDays, int FileSizeLimitMB, bool ArchiveEnabled);

/// <summary>Admin-only log viewing and configuration endpoints.</summary>
public static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logs", (long? after, int? limit, LogBufferSink buffer) =>
        {
            var entries = buffer.Query(after ?? 0, limit ?? 200);
            var nextSeq = entries.Count > 0 ? entries[^1].Seq : (after ?? 0);
            return Results.Ok(new { entries, nextSeq });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapGet("/api/logs/config", (LogsConfigService config) => Results.Ok(config.Current))
           .RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapPut("/api/logs/level", (SetLogLevelRequest req, LogsConfigService config, LoggingLevelSwitch levelSwitch) =>
        {
            if (!config.TryUpdateLevel(req.Level, out var error)) return Results.BadRequest(new { error });
            levelSwitch.MinimumLevel = LoggingConfig.ToSerilogLevel(config.Current.Level);
            return Results.Ok(config.Current);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        app.MapPut("/api/logs/config", (SetLogConfigRequest req, LogsConfigService config) =>
        {
            if (!config.TryUpdateConfig(req.RetentionDays, req.FileSizeLimitMB, req.ArchiveEnabled, out var error))
                return Results.BadRequest(new { error });
            return Results.Ok(config.Current);
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
