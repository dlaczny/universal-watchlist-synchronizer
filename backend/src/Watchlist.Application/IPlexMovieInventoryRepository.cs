namespace Watchlist.Application;

public interface IPlexMovieInventoryRepository
{
    Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
        IReadOnlyList<PlexMovieDto> movies,
        IReadOnlySet<string> scannedSectionKeys,
        DateTimeOffset syncTime,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexMovieDto>> GetUnmatchedMoviesAsync(CancellationToken cancellationToken);

    Task<PlexMovieDto?> GetMovieAsync(string ratingKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken);

    Task ApplyMatchUpdatesAsync(
        IReadOnlyList<PlexMovieMatchUpdate> updates,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}
