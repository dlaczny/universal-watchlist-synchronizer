namespace Watchlist.Application;

public interface ILetterboxdWatchlistClient
{
    Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken);
}
