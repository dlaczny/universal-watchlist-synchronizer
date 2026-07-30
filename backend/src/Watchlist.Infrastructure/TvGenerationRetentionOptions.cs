namespace Watchlist.Infrastructure;

public sealed class TvGenerationRetentionOptions
{
    public const string SectionName = "TvGenerationRetention";

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(7);

    public int MaxGenerations { get; init; } = 48;

    public TimeSpan OrphanGracePeriod { get; init; } = TimeSpan.FromDays(1);
}
