namespace Watchlist.Application;

/// <summary>
/// Provides one process-wide asynchronous Trakt operation lease.
/// </summary>
public sealed class TraktOperationCoordinator : ITraktOperationCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? ownedGate = gate;

        public ValueTask DisposeAsync()
        {
            SemaphoreSlim? release = Interlocked.Exchange(ref ownedGate, null);
            release?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
