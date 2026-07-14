namespace Watchlist.Application;

/// <summary>
/// Contains deterministic season-level watched progress for one Trakt show.
/// </summary>
public sealed record TraktDetailedShowProgress(
    int AiredEpisodes,
    int CompletedEpisodes,
    IReadOnlyList<TraktDetailedSeasonProgress> Seasons)
{
    private IReadOnlyList<TraktDetailedSeasonProgress> _seasons = Snapshot(Seasons);

    public IReadOnlyList<TraktDetailedSeasonProgress> Seasons
    {
        get => _seasons;
        init => _seasons = Snapshot(value);
    }

    private static IReadOnlyList<TraktDetailedSeasonProgress> Snapshot(
        IReadOnlyList<TraktDetailedSeasonProgress> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
