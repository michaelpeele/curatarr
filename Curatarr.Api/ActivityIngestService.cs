using Curatarr.Api.Hubs;
using Curatarr.Engine.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Curatarr.Api;

/// <summary>
/// Drains the <see cref="ActivityRecorder"/> channel and persists events in
/// small batches, then broadcasts them to Admin clients. Batching keeps the
/// SQLite write load low during a scan burst; the recorder's TryWrite means a
/// full channel drops events rather than ever blocking a user action.
/// </summary>
public sealed class ActivityIngestService : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxBatch = 50;
    private const int FailureLogIntervalMinutes = 5;

    private readonly ActivityRecorder _recorder;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<ActivityHub> _hub;
    private readonly ILogger<ActivityIngestService> _logger;

    private DateTime _lastFailureLog = DateTime.MinValue;

    public ActivityIngestService(
        ActivityRecorder recorder,
        IServiceScopeFactory scopes,
        IHubContext<ActivityHub> hub,
        ILogger<ActivityIngestService> logger)
    {
        _recorder = recorder;
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _recorder.Reader;
        var batch = new List<ActivityEvent>(MaxBatch);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();
                // Wait for the first event, then greedily take up to MaxBatch
                // that are already queued so a burst becomes one write.
                if (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false) == false)
                    break;

                while (batch.Count < MaxBatch && reader.TryRead(out var evt))
                    batch.Add(evt);

                if (batch.Count == 0)
                    continue;

                using (var scope = _scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<CuratarrDbContext>();
                    db.ActivityEvents.AddRange(batch);
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                }

                await _hub.Clients.All.SendAsync("ActivityCreated", batch, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                // Throttled so a persistent DB failure can't flood the very log
                // pipeline this feature also feeds.
                if (DateTime.UtcNow - _lastFailureLog > TimeSpan.FromMinutes(FailureLogIntervalMinutes))
                {
                    _lastFailureLog = DateTime.UtcNow;
                    _logger.LogError(ex, "Failed to persist/broadcast an activity batch ({Count} events)", batch.Count);
                }
                try
                {
                    await Task.Delay(FlushInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
