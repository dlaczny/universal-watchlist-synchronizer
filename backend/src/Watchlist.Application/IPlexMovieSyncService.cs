namespace Watchlist.Application;

public interface IPlexMovieSyncService
{
    Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken);
}
