using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Curatarr.Api;

/// <summary>One rendered log line held in the in-memory ring buffer.</summary>
public sealed record LogEntry(
    long Seq,
    string Timestamp,
    string Level,
    string SourceContext,
    string Message,
    string? Exception);

/// <summary>
/// Serilog sink that keeps the most recent 2,000 log events for the Logs page.
/// Each entry gets a monotonically increasing sequence so clients can ask for
/// only what arrived after their last read, and the broadcaster can push new
/// lines to /hubs/logs.
/// </summary>
public sealed class LogBufferSink : ILogEventSink
{
    public const int Capacity = 2000;
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private long _seq;

    /// <summary>Raised for every accepted entry; the hub broadcaster subscribes after build.</summary>
    public event Action<LogEntry>? EntryAdded;

    public long LatestSeq => Interlocked.Read(ref _seq);

    public void Emit(LogEvent logEvent)
    {
        var seq = Interlocked.Increment(ref _seq);
        var source = logEvent.Properties.TryGetValue("SourceContext", out var value)
            ? value.ToString().Trim('"')
            : "";
        var entry = new LogEntry(
            seq,
            logEvent.Timestamp.ToString(TimestampFormat),
            logEvent.Level.ToString(),
            source,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString());

        _entries.Enqueue(entry);
        while (_entries.Count > Capacity) _entries.TryDequeue(out _);
        EntryAdded?.Invoke(entry);
    }

    /// <summary>Newest-known entries with Seq greater than <paramref name="after"/>.</summary>
    public List<LogEntry> Query(long after, int limit)
    {
        if (limit <= 0) limit = 200;
        if (limit > 1000) limit = 1000;
        return _entries
            .Where(e => e.Seq > after)
            .OrderBy(e => e.Seq)
            .Take(limit)
            .ToList();
    }
}
