using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Orchestrates the Letterboxd movie watchlist sync.
/// </summary>
public sealed class LetterboxdMovieSyncService(
    ILetterboxdWatchlistClient client,
    IWatchlistWriteRepository repository,
    TimeProvider timeProvider,
    LetterboxdSyncGate syncGate) : ILetterboxdMovieSyncService
{
    private const string CompletedResultStatus = "completed";
    private const string CompletedRunStatus = "letterboxd_completed";

    /// <inheritdoc />
    public Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
    {
        return syncGate.RunAsync(
            () => SyncCoreAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<LetterboxdSyncResultDto> SyncCoreAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<LetterboxdMovieDto> sourceMovies = await client.GetMoviesAsync(cancellationToken);
        ValidateSourceMovies(sourceMovies);
        IReadOnlyList<WatchlistItem> existingItems = await repository.GetItemsAsync(cancellationToken);
        Dictionary<string, WatchlistItem> existingLetterboxdMovies = existingItems
            .Where(item => item.MediaType == MediaType.Movie && item.Source == WatchlistSource.Letterboxd)
            .ToDictionary(item => item.SourceId, StringComparer.Ordinal);

        List<WatchlistItemWriteModel> upsertItems = sourceMovies
            .Select(movie => ToWriteModel(movie, existingLetterboxdMovies, startedAt))
            .ToList();
        HashSet<string> sourceIds = sourceMovies
            .Select(movie => movie.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();
        LetterboxdMovieSyncApplyResult applyResult = await repository.ApplyLetterboxdMovieSyncAsync(
            upsertItems,
            sourceIds,
            CompletedRunStatus,
            finishedAt,
            cancellationToken);

        return new LetterboxdSyncResultDto(
            CompletedResultStatus,
            startedAt,
            finishedAt,
            sourceMovies.Count,
            upsertItems.Count,
            applyResult.ItemsMarkedWatched,
            applyResult.SourceSnapshotId);
    }

    private static void ValidateSourceMovies(IReadOnlyList<LetterboxdMovieDto> sourceMovies)
    {
        if (sourceMovies.Count == 0)
        {
            throw new LetterboxdSnapshotRejectedException(
                "Letterboxd watchlist returned no movies.");
        }

        HashSet<string> sourceIds = new(StringComparer.Ordinal);
        foreach (LetterboxdMovieDto movie in sourceMovies)
        {
            if (!int.TryParse(movie.SourceId, out int sourceId)
                || sourceId <= 0
                || string.IsNullOrWhiteSpace(movie.Title))
            {
                throw new LetterboxdSnapshotRejectedException(
                    "Letterboxd watchlist returned an invalid movie identity.");
            }

            if (!sourceIds.Add(movie.SourceId))
            {
                throw new LetterboxdSnapshotRejectedException(
                    "Letterboxd watchlist returned duplicate movie identities.");
            }
        }
    }

    private static WatchlistItemWriteModel ToWriteModel(
        LetterboxdMovieDto movie,
        IReadOnlyDictionary<string, WatchlistItem> existingMovies,
        DateTimeOffset syncTime)
    {
        existingMovies.TryGetValue(movie.SourceId, out WatchlistItem? existing);
        ReleaseStatus releaseStatus = ToReleaseStatus(movie.ReleaseYear, syncTime.Year);
        AvailabilityStatus availabilityStatus = existing?.AvailabilityStatus
            ?? ToInitialAvailabilityStatus(releaseStatus);

        WatchlistItem item = new(
            $"movie-letterboxd-{movie.SourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            movie.SourceId,
            movie.Title,
            movie.ReleaseYear,
            existing?.Overview,
            existing?.PosterUrl,
            existing?.BackdropUrl,
            releaseStatus,
            availabilityStatus,
            existing?.AddedAt ?? syncTime,
            syncTime);

        return new WatchlistItemWriteModel(item, movie.ImdbId, movie.LetterboxdPath);
    }

    private static ReleaseStatus ToReleaseStatus(int? releaseYear, int currentYear)
    {
        if (releaseYear is null)
        {
            return ReleaseStatus.Unknown;
        }

        return releaseYear > currentYear ? ReleaseStatus.Unreleased : ReleaseStatus.Released;
    }

    private static AvailabilityStatus ToInitialAvailabilityStatus(ReleaseStatus releaseStatus)
    {
        return releaseStatus switch
        {
            ReleaseStatus.Unreleased => AvailabilityStatus.Unreleased,
            ReleaseStatus.Unknown => AvailabilityStatus.UnknownMatch,
            _ => AvailabilityStatus.NotOnPlex
        };
    }
}
