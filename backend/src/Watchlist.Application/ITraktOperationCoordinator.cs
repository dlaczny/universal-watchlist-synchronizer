namespace Watchlist.Application;

/// <summary>
/// Serializes Trakt source reads and configured-account history mutations.
/// </summary>
public interface ITraktOperationCoordinator
{
    ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}
