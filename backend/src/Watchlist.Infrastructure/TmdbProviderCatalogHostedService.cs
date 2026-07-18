using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TmdbProviderCatalogHostedService(
    ITmdbTvMetadataClient tmdbClient,
    ITmdbProviderCatalogRepository repository,
    TimeProvider timeProvider,
    ILogger<TmdbProviderCatalogHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay = await RefreshIfDueAsync(stoppingToken);
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<TimeSpan> RefreshIfDueAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset? lastAttemptAt;
        try
        {
            lastAttemptAt = await repository.GetLastAttemptAtAsync(cancellationToken);
        }
        catch (MongoException)
        {
            LogFailure("mongo_unavailable");
            return RefreshInterval;
        }

        if (lastAttemptAt is DateTimeOffset attemptedAt)
        {
            TimeSpan age = now - attemptedAt;
            if (age < RefreshInterval && age >= TimeSpan.Zero)
            {
                return RefreshInterval - age;
            }
        }

        try
        {
            TmdbWatchProviderCatalogDto catalog = await tmdbClient.GetProviderCatalogAsync(
                cancellationToken);
            TmdbWatchProviderRegionsDto regions = await tmdbClient.GetProviderRegionsAsync(
                cancellationToken);
            DateTimeOffset completedAt = timeProvider.GetUtcNow().ToUniversalTime();
            if (catalog.FetchedAt > completedAt || regions.FetchedAt > completedAt)
            {
                throw new TmdbParseException("TMDB provider catalog timestamp was invalid.");
            }

            TmdbProviderCatalogSnapshot snapshot = new(
                catalog.FetchedAt,
                regions.FetchedAt,
                false,
                null,
                null,
                catalog.Providers,
                regions.RegionCodes);
            await repository.ReplaceAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TmdbUnavailableException)
        {
            await MarkFailureAsync("tmdb_unavailable", cancellationToken);
        }
        catch (TmdbParseException)
        {
            await MarkFailureAsync("tmdb_parse_error", cancellationToken);
        }
        catch (MongoException)
        {
            LogFailure("mongo_unavailable");
        }

        return RefreshInterval;
    }

    private async Task MarkFailureAsync(
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.MarkStaleAsync(
                code,
                timeProvider.GetUtcNow().ToUniversalTime(),
                cancellationToken);
            LogFailure(code);
        }
        catch (MongoException)
        {
            LogFailure("mongo_unavailable");
        }
    }

    private void LogFailure(string code)
    {
        logger.LogWarning(
            "TMDB provider catalog refresh failed with code {ErrorCode}.",
            code);
    }
}
