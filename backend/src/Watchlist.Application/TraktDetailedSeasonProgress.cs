namespace Watchlist.Application;

/// <summary>
/// Contains watched progress for one numbered Trakt season.
/// </summary>
public sealed record TraktDetailedSeasonProgress(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    IReadOnlyList<TraktDetailedEpisodeProgress> Episodes)
{
    private IReadOnlyList<TraktDetailedEpisodeProgress> _episodes = Snapshot(Episodes);

    public IReadOnlyList<TraktDetailedEpisodeProgress> Episodes
    {
        get => _episodes;
        init => _episodes = Snapshot(value);
    }

    private static IReadOnlyList<TraktDetailedEpisodeProgress> Snapshot(
        IReadOnlyList<TraktDetailedEpisodeProgress> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
