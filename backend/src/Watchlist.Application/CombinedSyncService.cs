using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class CombinedSyncService(
    ILetterboxdMovieSyncService letterboxdSyncService,
    ITmdbMovieEnrichmentService tmdbMovieEnrichmentService,
    IPlexMovieSyncService plexMovieSyncService,
    ITvSyncService tvSyncService,
    TimeProvider timeProvider) : ICombinedSyncService
{
    public async Task<CombinedSyncResultDto> SyncAllAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        LetterboxdSyncResultDto letterboxd = await letterboxdSyncService.SyncAsync(cancellationToken);
        TmdbMovieEnrichmentResultDto tmdb = await tmdbMovieEnrichmentService.SyncMoviesAsync(cancellationToken);
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        TvSyncResultDto tv;
        try
        {
            tv = await tvSyncService.SyncAsync(TvGenerationKind.ScheduledFull, cancellationToken);
        }
        catch (Exception exception) when (IsTypedTvFailure(exception))
        {
            DateTimeOffset failedAt = timeProvider.GetUtcNow();
            tv = new TvSyncResultDto(
                "failed",
                failedAt,
                failedAt,
                string.Empty,
                "scheduled_full",
                0,
                0,
                0,
                0,
                false,
                [ToHealthReason(exception)]);
        }

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new CombinedSyncResultDto(
            IsCompleted(letterboxd.Status) && IsCompleted(tmdb.Status) && IsCompleted(plex.Status) && IsCompleted(tv.Status)
                ? "completed"
                : "partial",
            startedAt,
            finishedAt,
            letterboxd,
            tmdb,
            tv,
            plex);
    }

    private static bool IsCompleted(string status) => string.Equals(status, "completed", StringComparison.Ordinal);

    private static bool IsTypedTvFailure(Exception exception) => exception is
        TraktNotConnectedException or
        TvSourceSnapshotRejectedException or
        TraktParseException or
        TmdbParseException or
        TraktUnavailableException or
        TraktConnectionUnreadableException;

    private static string ToHealthReason(Exception exception) => exception switch
    {
        TraktNotConnectedException => "trakt_not_connected",
        TvSourceSnapshotRejectedException => "tv_snapshot_rejected",
        TraktParseException => "trakt_malformed_response",
        TmdbParseException => "tmdb_malformed_response",
        TraktUnavailableException => "trakt_unavailable",
        TraktConnectionUnreadableException => "trakt_connection_unreadable",
        _ => "tv_sync_failed"
    };
}
