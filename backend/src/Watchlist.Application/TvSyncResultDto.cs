namespace Watchlist.Application;

public sealed record TvSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string GenerationId,
    string Kind,
    int WatchlistItemsFetched,
    int ProgressItemsFetched,
    int ShowsPublished,
    int ProviderFailures,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons)
{
    private IReadOnlyList<string> _healthReasons = Snapshot(HealthReasons);

    public IReadOnlyList<string> HealthReasons
    {
        get => _healthReasons;
        init => _healthReasons = Snapshot(value);
    }

    private static IReadOnlyList<string> Snapshot(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
