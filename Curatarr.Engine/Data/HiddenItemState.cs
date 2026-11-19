namespace Curatarr.Engine.Data;

/// <summary>
/// Shared rules for the two-phase (request → Sync) hide state. A HiddenItem row
/// is a *request*: it is created when the user clicks Hide and cleared only by a
/// successful reconcile after an unhide request. The grid and the symlink engine
/// need slightly different views of that row:
///
/// - The grid shows a title as hidden only once a Sync has applied the hide, or
///   while an unhide request is still waiting for Sync (the title is still
///   physically hidden in that window).
/// - The reconcile applies every row that has no pending unhide request, which
///   includes hides requested since the last Sync.
/// </summary>
public static class HiddenItemState
{
    /// <summary>
    /// Is the title hidden from the user's point of view right now? True for a
    /// hide applied by a previous Sync, and for a still pending unhide. A hide
    /// requested after the last Sync is deliberately false — it stays visible
    /// (with a yellow eye) until Sync applies it.
    /// </summary>
    public static bool IsEffectiveHidden(
        DateTime createdAt,
        DateTime? unhideRequestedAt,
        DateTime? lastReconciledAt)
    {
        if (unhideRequestedAt is not null)
            return lastReconciledAt is null || unhideRequestedAt > lastReconciledAt.Value;

        return lastReconciledAt is not null && createdAt <= lastReconciledAt.Value;
    }

    /// <summary>
    /// Timestamp the card eye should compare against LastReconciledAt. For a
    /// pending unhide the unhide request is the new state change, so using the
    /// original hide stamp would incorrectly show a green eye.
    /// </summary>
    public static DateTime StateStamp(DateTime createdAt, DateTime? unhideRequestedAt) =>
        unhideRequestedAt ?? createdAt;

    /// <summary>
    /// Should the symlink engine treat this row as hidden during the current
    /// reconcile? Unhide requests are honoured in the same pass (the row is
    /// cleared afterwards), so only rows without one are skipped.
    /// </summary>
    public static bool AppliesDuringReconcile(DateTime? unhideRequestedAt) =>
        unhideRequestedAt is null;
}
