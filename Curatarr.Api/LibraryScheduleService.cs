using Curatarr.Engine;
using Curatarr.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Evaluates every library's schedule once a minute and reconciles only the
/// libraries whose active state flipped (or that have never been evaluated
/// since startup). Runs a full pass 15s after boot so links pruned during an
/// earlier inactive window are cleaned up after a restart, and shares
/// <see cref="ReconcileGate"/> with the manual Sync endpoint.
/// </summary>
public sealed class LibraryScheduleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ScheduleTimeZone _timeZone;
    private readonly PlexService _plex;
    private readonly ILogger<LibraryScheduleService> _logger;
    private readonly Dictionary<string, bool> _lastState = new();

    public LibraryScheduleService(
        IServiceScopeFactory scopes, ScheduleTimeZone timeZone, PlexService plex,
        ILogger<LibraryScheduleService> logger)
    {
        _scopes = scopes;
        _timeZone = timeZone;
        _plex = plex;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EvaluateAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Library schedule evaluation failed."); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<SymlinkEngine>();

        var libraries = await db.VirtualLibraries.AsNoTracking().ToListAsync(ct);
        var schedules = await db.LibrarySchedules.AsNoTracking().ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var library in libraries)
        {
            var rules = schedules.Where(s => s.VirtualLibraryId == library.Id).ToList();
            var active = ScheduleEngine.IsActive(rules, now, _timeZone.Zone);
            var known = _lastState.TryGetValue(library.Id, out var previous);
            if (known && previous == active) continue; // steady state -- no disk work

            try
            {
                var report = await engine.ReconcileLibraryAsync(library.Id, active, ct);
                _lastState[library.Id] = active;
                _logger.LogInformation(
                    "Schedule {State} for '{Library}' ({Zone}): {Created} created, {Removed} removed, {Broken} broken.",
                    active ? "active" : "inactive", library.Name, _timeZone.Id,
                    report.Created.Count, report.Removed.Count, report.Broken.Count);

                // Only when this tick actually reconciled the library.
                _plex.TriggerInBackground([library.Id]);
            }
            catch (Exception ex)
            {
                // Leave the state unknown so the next tick retries.
                _lastState.Remove(library.Id);
                _logger.LogError(ex, "Schedule reconcile failed for '{Library}'.", library.Name);
            }
        }

        var liveIds = libraries.Select(l => l.Id).ToHashSet();
        foreach (var stale in _lastState.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _lastState.Remove(stale);
    }
}
