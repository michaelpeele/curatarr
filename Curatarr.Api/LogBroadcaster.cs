using Curatarr.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Curatarr.Api;

/// <summary>
/// Bridges the in-memory log ring to the Admin-only Logs hub. Attached after
/// app.Build() because IHubContext is not available while Serilog is being
/// configured. The sink holds the delegate, keeping this instance alive.
/// </summary>
public sealed class LogBroadcaster : IDisposable
{
    private readonly LogBufferSink _sink;
    private readonly IHubContext<LogsHub> _hub;

    public LogBroadcaster(LogBufferSink sink, IHubContext<LogsHub> hub)
    {
        _sink = sink;
        _hub = hub;
        _sink.EntryAdded += OnEntry;
    }

    private async void OnEntry(LogEntry entry)
    {
        try
        {
            await _hub.Clients.All.SendAsync("LogEntry", entry);
        }
        catch
        {
            // Never let hub failures interfere with logging (or recurse).
        }
    }

    public void Dispose() => _sink.EntryAdded -= OnEntry;
}
