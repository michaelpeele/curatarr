using Curatarr.Api;
using Serilog;
using Xunit;

namespace Curatarr.Tests;

public class LogBufferSinkTests
{
    [Fact]
    public void Emit_assigns_monotonic_sequence_numbers()
    {
        var sink = new LogBufferSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Information("first");
        logger.Information("second");

        var entries = sink.Query(0, 10);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Seq);
        Assert.Equal(2, entries[1].Seq);
        Assert.Equal(2, sink.LatestSeq);
    }

    [Fact]
    public void Ring_buffer_caps_at_2000_and_drops_the_oldest_entries()
    {
        var sink = new LogBufferSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        var emitted = LogBufferSink.Capacity + 10;
        for (var i = 1; i <= emitted; i++)
            logger.Information("Event {Number}", i);

        Assert.Equal(emitted, sink.LatestSeq);

        // Query is capped at 1000 rows, so page through the whole ring.
        var firstPage = sink.Query(0, 1000);
        var secondPage = sink.Query(firstPage[^1].Seq, 1000);
        Assert.Equal(1000, firstPage.Count);
        Assert.Equal(1000, secondPage.Count);
        Assert.Equal(LogBufferSink.Capacity, firstPage.Count + secondPage.Count);
        Assert.Equal(emitted - LogBufferSink.Capacity + 1, firstPage[0].Seq);
        Assert.Equal(emitted, secondPage[^1].Seq);
    }

    [Fact]
    public void Emit_renders_message_level_and_exception()
    {
        var sink = new LogBufferSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Error(new InvalidOperationException("boom"), "Operation failed");

        var entry = Assert.Single(sink.Query(0, 10));
        Assert.Equal("Error", entry.Level);
        Assert.Contains("Operation failed", entry.Message);
        Assert.False(string.IsNullOrWhiteSpace(entry.Timestamp));
        Assert.NotNull(entry.Exception);
        Assert.Contains("InvalidOperationException", entry.Exception);
        Assert.Contains("boom", entry.Exception);
    }

    [Fact]
    public void Entry_added_is_raised_for_each_accepted_entry()
    {
        var sink = new LogBufferSink();
        var seen = new List<long>();
        sink.EntryAdded += entry => seen.Add(entry.Seq);
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Information("one");
        logger.Information("two");

        Assert.Equal(new long[] { 1, 2 }, seen);
    }
}
