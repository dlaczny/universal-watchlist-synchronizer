namespace Watchlist.Infrastructure;

public sealed class DefaultHttpRetryDelay(TimeProvider timeProvider) : IHttpRetryDelay
{
    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8)
    ];

    public async Task DelayAsync(
        int attempt,
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = GetDelay(attempt, response);
        await Task.Delay(delay, timeProvider, cancellationToken);
    }

    private static TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        TimeSpan? retryAfter = response?.Headers.RetryAfter?.Delta;
        if (retryAfter is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        DateTimeOffset? retryAfterDate = response?.Headers.RetryAfter?.Date;
        if (retryAfterDate is { } date)
        {
            TimeSpan dateDelay = date - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay;
            }
        }

        int index = Math.Clamp(attempt - 1, 0, BackoffDelays.Length - 1);
        return BackoffDelays[index];
    }
}
