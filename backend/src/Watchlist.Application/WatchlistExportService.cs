namespace Watchlist.Application;

public sealed class WatchlistExportService(IWatchlistExportRepository repository)
{
    public async Task<IReadOnlyList<RadarrMovieExportItemDto>> GetRadarrMoviesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistExportMovieModel> movies =
            await repository.GetLetterboxdMoviesAsync(cancellationToken);

        return movies
            .Where(movie => movie.OwnedServiceAvailability.Count == 0)
            .Select(ToRadarrItemOrNull)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public Task<IReadOnlyList<object>> GetSonarrTvAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<object>>([]);
    }

    private static RadarrMovieExportItemDto? ToRadarrItemOrNull(WatchlistExportMovieModel movie)
    {
        if (!int.TryParse(movie.SourceId, out int sourceId))
        {
            return null;
        }

        return new RadarrMovieExportItemDto(
            sourceId,
            movie.ImdbId ?? string.Empty,
            movie.Title,
            movie.Year?.ToString() ?? string.Empty,
            movie.LetterboxdPath ?? string.Empty,
            false);
    }
}
