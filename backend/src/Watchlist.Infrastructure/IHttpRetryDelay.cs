namespace Watchlist.Infrastructure;

public interface IHttpRetryDelay
{
    Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken cancellationToken);
}
