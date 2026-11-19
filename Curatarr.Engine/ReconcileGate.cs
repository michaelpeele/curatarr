namespace Curatarr.Engine;

/// <summary>
/// Serializes symlink reconciliation across callers. The manual/admin
/// reconcile and the schedule worker both mutate the same SQLite ledger and
/// the same output symlink tree; letting them overlap can duplicate records or
/// delete links another pass just created. Both entry points acquire this gate
/// (a single in-process semaphore — Curatarr runs as one API instance).
/// </summary>
public sealed class ReconcileGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for exclusive access; dispose the result to release it.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
