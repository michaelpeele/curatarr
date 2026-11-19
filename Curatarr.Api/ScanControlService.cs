namespace Curatarr.Api;

/// <summary>
/// Coordinates pause/stop for whatever scan is currently running.
/// Single-scan-at-a-time model (matches Sonarr/Radarr's own UX -- one
/// active library scan, not a queue of concurrent scans). Also tracks live
/// progress and timing so the Activity page can show an elapsed/ETA countdown.
/// </summary>
public class ScanControlService
{
    private readonly object _lock = new();
    private CancellationTokenSource _cts = new();
    private TaskCompletionSource? _pauseTcs;

    // Live progress/timing for GET /api/activity/scan-status. All reads and
    // writes go through _lock so the snapshot is internally consistent.
    private bool _running;
    private DateTimeOffset? _startedAt;
    private int _completed;
    private int _total;
    private DateTimeOffset? _lastProgressAt;
    private bool _paused;
    private bool _stopped;

    public CancellationToken BeginNewScan()
    {
        lock (_lock)
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            _pauseTcs = null;
            _running = true;
            _startedAt = DateTimeOffset.UtcNow;
            _completed = 0;
            _total = 0;
            _lastProgressAt = null;
            _paused = false;
            _stopped = false;
            return _cts.Token;
        }
    }

    /// <summary>Called from the scan's onProgress callback as items complete.</summary>
    public void ReportProgress(int completed, int total)
    {
        lock (_lock)
        {
            _completed = completed;
            _total = total;
            _lastProgressAt = DateTimeOffset.UtcNow;
            _paused = _pauseTcs is not null;
        }
    }

    /// <summary>Mark the scan as finished (called in a finally block by /api/scan).</summary>
    public void Finish()
    {
        lock (_lock)
        {
            _running = false;
            _paused = false;
            _lastProgressAt = DateTimeOffset.UtcNow;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts.Cancel();
            _pauseTcs?.TrySetCanceled();
            _stopped = true;
            _running = false;
            _paused = false;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _pauseTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _paused = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _pauseTcs?.TrySetResult();
            _pauseTcs = null;
            _paused = false;
        }
    }

    public bool IsPaused { get { lock (_lock) return _pauseTcs is not null; } }

    public async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        while (true)
        {
            Task? gate;
            lock (_lock) { gate = _pauseTcs?.Task; }
            if (gate is null) return;
            await gate.WaitAsync(ct);
        }
    }

    /// <summary>Immutable point-in-time view for the scan-status endpoint.</summary>
    public ScanStatusSnapshot Snapshot()
    {
        lock (_lock)
        {
            var elapsed = _startedAt is null
                ? TimeSpan.Zero
                : DateTimeOffset.UtcNow - _startedAt.Value;

            double? etaSeconds = null;
            if (_running && !_paused && _completed > 0 && _total > _completed)
            {
                var perItem = elapsed.TotalSeconds / _completed;
                etaSeconds = perItem * (_total - _completed);
            }

            return new ScanStatusSnapshot(
                _running,
                _startedAt,
                _completed,
                _total,
                _paused,
                _stopped,
                elapsed.TotalSeconds,
                etaSeconds);
        }
    }
}

/// <summary>Live state of the current (or last) scan. ETA is null until at
/// least one item has completed and no ETA is offered while paused.</summary>
public sealed record ScanStatusSnapshot(
    bool Running,
    DateTimeOffset? StartedAt,
    int Completed,
    int Total,
    bool Paused,
    bool Stopped,
    double ElapsedSeconds,
    double? EtaSeconds);
