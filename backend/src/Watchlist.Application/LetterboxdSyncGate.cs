namespace Watchlist.Application;

public sealed class LetterboxdSyncGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> RunAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }
}
