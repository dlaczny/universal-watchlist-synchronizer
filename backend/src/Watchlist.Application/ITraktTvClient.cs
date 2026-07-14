namespace Watchlist.Application;

/// <summary>
/// Reads the complete authenticated Trakt state required by TV publication.
/// </summary>
public interface ITraktTvClient
{
    Task<TraktActivityCursor> GetLastActivitiesAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktWatchlistShow>> GetWatchlistAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktWatchedShowProgress>> GetWatchedProgressAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<TraktDetailedShowProgress> GetDetailedProgressAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken);

    Task<TraktShowMetadata> GetShowMetadataAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktSeasonEpisode>> GetSeasonAsync(
        string accessToken,
        long traktId,
        int seasonNumber,
        CancellationToken cancellationToken);
}
