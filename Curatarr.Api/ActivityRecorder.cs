using System.Security.Claims;
using System.Threading.Channels;
using Curatarr.Engine.Data;

namespace Curatarr.Api;

/// <summary>
/// Fire-and-forget recorder for the Activity audit log. Callers never block on
/// disk or SignalR: an event is redacted, stamped and dropped into a bounded
/// channel, and <see cref="ActivityIngestService"/> persists/broadcasts it in
/// batches. When the channel is full the event is dropped rather than slowing
/// a user action — Activity is informational, never part of a transaction.
/// </summary>
public interface IActivityRecorder
{
    void Record(
        ActivityCategory category,
        string type,
        string actor,
        string detail,
        string? metadata = null);
}

public sealed class ActivityRecorder : IActivityRecorder
{
    private readonly Channel<ActivityEvent> _channel;

    public ActivityRecorder()
    {
        _channel = Channel.CreateBounded<ActivityEvent>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Read side, consumed only by <see cref="ActivityIngestService"/>.</summary>
    public ChannelReader<ActivityEvent> Reader => _channel.Reader;

    public void Record(
        ActivityCategory category,
        string type,
        string actor,
        string detail,
        string? metadata = null)
    {
        var evt = new ActivityEvent
        {
            Id = Guid.NewGuid().ToString(),
            OccurredAt = DateTimeOffset.UtcNow,
            Category = category,
            Type = type,
            Actor = string.IsNullOrWhiteSpace(actor) ? "System" : actor,
            Detail = ActivityRedaction.Sanitize(detail),
            Metadata = metadata is null ? null : ActivityRedaction.Sanitize(metadata),
        };

        // TryWrite is non-blocking; with FullMode.Wait it fails immediately
        // when the bounded channel is full, so a busy server never stalls UI.
        _channel.Writer.TryWrite(evt);
    }

    /// <summary>The signed-in user's email, or a background-service actor.</summary>
    public static string ActorFrom(ClaimsPrincipal? principal) =>
        principal?.Identity?.Name ?? "System";
}
