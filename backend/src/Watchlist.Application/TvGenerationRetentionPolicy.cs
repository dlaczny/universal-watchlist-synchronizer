namespace Watchlist.Application;

/// <summary>
/// Defines bounded retention limits for immutable TV generations.
/// </summary>
public sealed record TvGenerationRetentionPolicy
{
    public TvGenerationRetentionPolicy(
        TimeSpan maxAge,
        int maxGenerations,
        TimeSpan orphanGracePeriod)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        }

        if (maxGenerations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGenerations));
        }

        if (orphanGracePeriod < TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(orphanGracePeriod));
        }

        MaxAge = maxAge;
        MaxGenerations = maxGenerations;
        OrphanGracePeriod = orphanGracePeriod;
    }

    public TimeSpan MaxAge { get; }

    public int MaxGenerations { get; }

    public TimeSpan OrphanGracePeriod { get; }
}
