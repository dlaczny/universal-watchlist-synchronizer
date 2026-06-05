namespace Watchlist.Application;

public interface IPlexLibraryClient
{
    Task<IReadOnlyList<PlexLibrarySectionDto>> GetSectionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(
        PlexLibrarySectionDto section,
        CancellationToken cancellationToken);
}
