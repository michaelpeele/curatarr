using Curatarr.Engine.Data;
using Xunit;

namespace Curatarr.Tests;

public class HiddenItemStateTests
{
    private static readonly DateTime SyncedAt = new(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Hide_requested_after_sync_stays_visible_until_next_sync()
    {
        var hideAt = SyncedAt.AddMinutes(5);

        Assert.False(HiddenItemState.IsEffectiveHidden(hideAt, null, SyncedAt));
        Assert.Equal(hideAt, HiddenItemState.StateStamp(hideAt, null));
        Assert.True(HiddenItemState.AppliesDuringReconcile(null));
    }

    [Fact]
    public void Hide_requested_before_sync_is_effective_hidden()
    {
        var hideAt = SyncedAt.AddMinutes(-5);

        Assert.True(HiddenItemState.IsEffectiveHidden(hideAt, null, SyncedAt));
        Assert.Equal(hideAt, HiddenItemState.StateStamp(hideAt, null));
    }

    [Fact]
    public void Unhide_requested_after_sync_remains_hidden_but_is_applied_by_sync()
    {
        var hideAt = SyncedAt.AddMinutes(-30);
        var unhideAt = SyncedAt.AddMinutes(5);

        Assert.True(HiddenItemState.IsEffectiveHidden(hideAt, unhideAt, SyncedAt));
        Assert.Equal(unhideAt, HiddenItemState.StateStamp(hideAt, unhideAt));
        Assert.False(HiddenItemState.AppliesDuringReconcile(unhideAt));
    }

    [Fact]
    public void Completed_unhide_is_no_longer_hidden()
    {
        var hideAt = SyncedAt.AddMinutes(-30);
        var unhideAt = SyncedAt.AddMinutes(-5);

        Assert.False(HiddenItemState.IsEffectiveHidden(hideAt, unhideAt, SyncedAt));
    }

    [Fact]
    public void Without_any_sync_a_hide_request_is_still_pending()
    {
        var hideAt = SyncedAt.AddMinutes(5);

        Assert.False(HiddenItemState.IsEffectiveHidden(hideAt, null, null));
        Assert.True(HiddenItemState.IsEffectiveHidden(hideAt, hideAt.AddMinutes(1), null));
    }
}
