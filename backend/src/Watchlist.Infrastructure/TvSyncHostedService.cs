using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

/// <summary>
/// Polls relevant Trakt activity and publishes complete TV generations when due.
/// </summary>
public sealed class TvSyncHostedService(
    ITraktConnectionService connectionService,
    ITraktAccessTokenProvider accessTokenProvider,
    ITraktTvClient traktClient,
    ITvGenerationRepository generationRepository,
    ITvSyncService syncService,
    TimeProvider timeProvider,
    IOptions<TraktOptions> options,
    ILogger<TvSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TraktOptions configured = options.Value;
        using PeriodicTimer timer = new(configured.ActivityPollInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunCycleAsync(configured, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LogFailure(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunCycleAsync(
        TraktOptions configured,
        CancellationToken cancellationToken)
    {
        TraktConnectionStatusDto connection = await connectionService
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        PublishedTvGeneration? published = await generationRepository
            .GetPublishedAsync(cancellationToken)
            .ConfigureAwait(false);
        TraktActivityCursor? currentActivity = null;
        if (string.Equals(connection.Status, "connected", StringComparison.Ordinal))
        {
            string accessToken = await accessTokenProvider
                .GetValidAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            currentActivity = await traktClient
                .GetLastActivitiesAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);
        }

        TvGenerationKind? kind = TvSyncSchedule.Decide(
            connection,
            published?.Manifest,
            currentActivity,
            timeProvider.GetUtcNow().ToUniversalTime(),
            configured.FullSyncInterval);
        if (kind is TvGenerationKind refreshKind)
        {
            await syncService.SyncAsync(refreshKind, cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogFailure(Exception exception)
    {
        string errorCode = exception switch
        {
            TvPublishedGenerationInvalidException invalid => invalid.Code,
            TvSourceSnapshotRejectedException rejected when IsStableCode(rejected.Message) =>
                rejected.Message,
            TraktNotConnectedException => "trakt_not_connected",
            TraktConnectionUnreadableException => "token_unreadable",
            TraktRefreshRejectedException => "trakt_refresh_rejected",
            TraktRateLimitedException => "trakt_rate_limited",
            TraktUnavailableException => "trakt_unavailable",
            TraktParseException => "trakt_parse_error",
            TraktPersistenceUnavailableException => "trakt_persistence_unavailable",
            TmdbUnavailableException => "tmdb_unavailable",
            TmdbParseException => "tmdb_parse_error",
            MongoException => "mongo_unavailable",
            _ => "tv_sync_unexpected"
        };
        logger.LogWarning(
            "TV refresh failed with code {ErrorCode} and exception type {ExceptionType}.",
            errorCode,
            exception.GetType().Name);
    }

    private static bool IsStableCode(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.All(character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_');
    }
}
