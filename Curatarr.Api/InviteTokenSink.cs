namespace Curatarr.Api;

// Test-only capture of one-time invite links. Constructed with enabled=false
// in normal/prod runs, so no tokens are ever retained outside the sandbox.
public sealed class InviteTokenSink
{
    private const int MaxCaptures = 50;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private readonly List<InviteCapture> _captures = new();

    public InviteTokenSink(bool enabled) => _enabled = enabled;

    public bool Enabled => _enabled;

    public void Capture(string email, string userId, string token, string link)
    {
        if (!_enabled) return;
        lock (_gate)
        {
            _captures.Add(new InviteCapture(email, userId, token, link, DateTimeOffset.UtcNow));
            if (_captures.Count > MaxCaptures)
                _captures.RemoveRange(0, _captures.Count - MaxCaptures);
        }
    }

    public IReadOnlyList<InviteCapture> Snapshot()
    {
        if (!_enabled) return Array.Empty<InviteCapture>();
        lock (_gate) return _captures.ToArray();
    }
}

public sealed record InviteCapture(
    string Email, string UserId, string Token, string Link, DateTimeOffset CapturedAt);
