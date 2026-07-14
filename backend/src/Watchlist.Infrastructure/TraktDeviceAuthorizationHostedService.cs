using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

/// <summary>
/// Advances pending Trakt device authorization on a durable one-second cadence.
/// </summary>
public sealed class TraktDeviceAuthorizationHostedService(
    ITraktConnectionRepository repository,
    ITraktConnectionService connectionService,
    TimeProvider timeProvider,
    ILogger<TraktDeviceAuthorizationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollCadence = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollIfDueAsync(stoppingToken);
                }
                catch (TraktUnavailableException)
                {
                    LogOperationalError("trakt_unavailable");
                }
                catch (TraktParseException)
                {
                    LogOperationalError("trakt_parse_error");
                }
                catch (TraktNotConnectedException)
                {
                    LogOperationalError("trakt_not_connected");
                }
                catch (TraktConnectionUnreadableException)
                {
                    LogOperationalError("token_unreadable");
                }
                catch (TraktDeviceAuthorizationException exception)
                {
                    LogOperationalError(exception.Code);
                }
                catch (MongoException)
                {
                    LogOperationalError("mongo_unavailable");
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    LogOperationalError("persistence_timeout");
                }

                await Task.Delay(PollCadence, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollIfDueAsync(CancellationToken cancellationToken)
    {
        TraktConnection? connection = await repository.GetAsync(cancellationToken);
        if (connection is null || connection.State != "pending")
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        bool hasExpired = connection.DeviceCodeExpiresAt is null
            || connection.DeviceCodeExpiresAt <= now;
        bool isDue = connection.NextDevicePollAt is null
            || connection.NextDevicePollAt <= now;
        if (!hasExpired && !isDue)
        {
            return;
        }

        await connectionService.PollPendingAsync(cancellationToken);
    }

    private void LogOperationalError(string code)
    {
        logger.LogWarning(
            "Trakt device authorization polling failed with code {ErrorCode}.",
            code);
    }
}
