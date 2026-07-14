namespace Watchlist.Application;

/// <summary>
/// Indicates that Trakt rate limited a read and may have supplied a retry delay.
/// </summary>
public sealed class TraktRateLimitedException(TimeSpan? retryAfter)
    : Exception("Trakt rate limited the request.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
