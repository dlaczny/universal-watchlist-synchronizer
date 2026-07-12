using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class WatchlistExportService(
    IWatchlistExportRepository repository,
    ISyncStatusReadRepository syncStatusRepository,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RadarrMovieExportItemDto>> GetRadarrMoviesAsync(
        CancellationToken cancellationToken)
    {
        WatchlistMovieLifecycleExport lifecycle =
            await repository.GetMovieLifecycleAsync(cancellationToken);

        return lifecycle.ActiveMovies
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

    public async Task<WorkerMovieSnapshotDto> GetMovieSyncSnapshotAsync(
        CancellationToken cancellationToken)
    {
        WatchlistMovieLifecycleExport lifecycle =
            await repository.GetMovieLifecycleAsync(cancellationToken);
        SyncStatusDto? latestMovieSync = await syncStatusRepository.GetLatestByStatusAsync(
            SyncRunStatuses.PlexMoviesCompleted,
            cancellationToken);

        return new WorkerMovieSnapshotDto(
            lifecycle.SourceSnapshot?.SnapshotId ?? string.Empty,
            timeProvider.GetUtcNow(),
            latestMovieSync?.LastSuccessfulSyncAt,
            lifecycle.ActiveMovies.Select(ToWorkerMovie).ToList(),
            lifecycle.WatchedMovies.Select(ToWorkerWatchedMovie).ToList());
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

    private static WorkerMovieDto ToWorkerMovie(WatchlistExportMovieModel movie)
    {
        int? tmdbId = ResolveTmdbId(movie);
        (bool eligible, string reason) = GetRadarrEligibility(movie, tmdbId);

        return new WorkerMovieDto(
            tmdbId,
            movie.ImdbId,
            movie.Title,
            movie.Year,
            movie.SourceId,
            movie.MetadataStatus,
            ToApiValue(movie.AvailabilityStatus),
            movie.OwnedServiceAvailability,
            eligible,
            reason);
    }

    private static WorkerWatchedMovieDto ToWorkerWatchedMovie(
        WatchlistWatchedMovieModel movie)
    {
        return new WorkerWatchedMovieDto(
            movie.TmdbId,
            movie.ImdbId,
            movie.Title,
            movie.Year,
            movie.SourceId,
            movie.WatchedAt,
            movie.LifecycleVersion,
            movie.LifecycleEventId);
    }

    private static int? ResolveTmdbId(WatchlistExportMovieModel movie)
    {
        if (movie.TmdbId is > 0)
        {
            return movie.TmdbId;
        }

        return int.TryParse(movie.SourceId, out int sourceId) && sourceId > 0
            ? sourceId
            : null;
    }

    private static (bool Eligible, string Reason) GetRadarrEligibility(
        WatchlistExportMovieModel movie,
        int? tmdbId)
    {
        if (tmdbId is null)
        {
            return (false, "invalid_tmdb_id");
        }

        if (!string.Equals(movie.MetadataStatus, "enriched", StringComparison.Ordinal))
        {
            return (false, "metadata_not_enriched");
        }

        if (movie.OwnedServiceAvailability.Count > 0)
        {
            return (false, "owned_service_available");
        }

        return (true, "no_owned_service");
    }

    private static string ToApiValue(AvailabilityStatus status)
    {
        return status switch
        {
            AvailabilityStatus.AvailableOnPlex => "available_on_plex",
            AvailabilityStatus.NotOnPlex => "not_on_plex",
            AvailabilityStatus.Unreleased => "unreleased",
            AvailabilityStatus.UnknownMatch => "unknown_match",
            _ => "unspecified"
        };
    }
}
