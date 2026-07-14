namespace Watchlist.Domain;

public sealed record TvSeasonProgress(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    bool HasKnownFutureEpisode,
    TvProviderAvailability Availability,
    IReadOnlyList<TvEpisodeProgress> Episodes)
{
    private IReadOnlyList<TvEpisodeProgress> _episodes = Snapshot(Episodes);

    public IReadOnlyList<TvEpisodeProgress> Episodes
    {
        get => _episodes;
        init => _episodes = Snapshot(value);
    }

    private static IReadOnlyList<TvEpisodeProgress> Snapshot(IReadOnlyList<TvEpisodeProgress> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
