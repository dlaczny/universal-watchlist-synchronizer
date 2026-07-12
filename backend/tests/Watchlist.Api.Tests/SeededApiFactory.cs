using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class SeededApiFactory(
    Exception? letterboxdSyncException = null,
    bool tmdbSingleMovieReturnsNull = false,
    Exception? tmdbMovieSyncException = null,
    Exception? tmdbSingleMovieSyncException = null,
    Exception? plexMovieSyncException = null,
    Exception? combinedSyncException = null,
    Exception? availabilityRefreshException = null,
    Exception? tmdbTvSyncException = null,
    string? syncApiKey = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (syncApiKey is not null)
        {
            builder.UseSetting("Sync:ApiKey", syncApiKey);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWatchlistReadRepository>();
            services.RemoveAll<ISyncStatusReadRepository>();
            services.RemoveAll<ILetterboxdMovieSyncService>();
            services.RemoveAll<ITmdbMovieEnrichmentService>();
            services.RemoveAll<IPlexMovieSyncService>();
            services.RemoveAll<IPlexMovieInventoryRepository>();
            services.RemoveAll<ICombinedSyncService>();
            services.RemoveAll<ITmdbTvWatchlistSyncService>();
            services.RemoveAll<IAvailabilityRefreshService>();
            services.RemoveAll<IWatchlistExportRepository>();
            RemoveBootstrapHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();
            services.AddSingleton<ISyncStatusReadRepository, SeededSyncStatusReadRepository>();
            services.AddSingleton<ILetterboxdMovieSyncService>(
                _ => new SeededLetterboxdMovieSyncService(letterboxdSyncException));
            services.AddSingleton<ITmdbMovieEnrichmentService>(
                _ => new SeededTmdbMovieEnrichmentService(
                    tmdbSingleMovieReturnsNull,
                    tmdbMovieSyncException,
                    tmdbSingleMovieSyncException));
            services.AddSingleton<IPlexMovieSyncService>(
                _ => new SeededPlexMovieSyncService(plexMovieSyncException));
            services.AddSingleton<IPlexMovieInventoryRepository, SeededPlexMovieInventoryRepository>();
            services.AddSingleton<ICombinedSyncService>(
                _ => new SeededCombinedSyncService(combinedSyncException));
            services.AddSingleton<ITmdbTvWatchlistSyncService>(
                _ => new SeededTmdbTvWatchlistSyncService(tmdbTvSyncException));
            services.AddSingleton<IAvailabilityRefreshService>(
                _ => new SeededAvailabilityRefreshService(availabilityRefreshException));
            services.AddSingleton<IWatchlistExportRepository, SeededWatchlistExportRepository>();
        });
    }

    internal static void RemoveBootstrapHostedService(IServiceCollection services)
    {
        ServiceDescriptor? bootstrapDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(MongoBootstrapHostedService));

        if (bootstrapDescriptor is not null)
        {
            services.Remove(bootstrapDescriptor);
        }
    }

    private sealed class SeededSyncStatusReadRepository : ISyncStatusReadRepository
    {
        public Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<SyncStatusDto?>(
                new SyncStatusDto("seeded", DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")));
        }

        public Task<SyncStatusDto?> GetLatestByStatusAsync(string status, CancellationToken cancellationToken)
        {
            return Task.FromResult<SyncStatusDto?>(
                new SyncStatusDto(status, DateTimeOffset.Parse("2026-06-05T12:00:00Z")));
        }
    }

    private sealed class SeededLetterboxdMovieSyncService(Exception? syncException) : ILetterboxdMovieSyncService
    {
        public Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
        {
            if (syncException is not null)
            {
                return Task.FromException<LetterboxdSyncResultDto>(syncException);
            }

            LetterboxdSyncResultDto result = new(
                "completed",
                DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-03T12:00:01Z"),
                2,
                2,
                0,
                "letterboxd-snapshot");

            return Task.FromResult(result);
        }
    }

    private sealed class SeededTmdbMovieEnrichmentService(
        bool singleMovieReturnsNull,
        Exception? movieSyncException,
        Exception? singleMovieSyncException) : ITmdbMovieEnrichmentService
    {
        public Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            if (movieSyncException is not null)
            {
                return Task.FromException<TmdbMovieEnrichmentResultDto>(movieSyncException);
            }

            TmdbMovieEnrichmentResultDto result = new(
                "completed",
                DateTimeOffset.Parse("2026-06-04T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-04T12:00:01Z"),
                2,
                2,
                0,
                0);

            return Task.FromResult(result);
        }

        public Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(
            string id,
            CancellationToken cancellationToken)
        {
            if (singleMovieSyncException is not null)
            {
                return Task.FromException<TmdbSingleMovieEnrichmentResultDto?>(singleMovieSyncException);
            }

            if (singleMovieReturnsNull)
            {
                return Task.FromResult<TmdbSingleMovieEnrichmentResultDto?>(null);
            }

            TmdbSingleMovieEnrichmentResultDto result = new("enriched", id, 1297842);

            return Task.FromResult<TmdbSingleMovieEnrichmentResultDto?>(result);
        }
    }

    private sealed class SeededPlexMovieSyncService(Exception? syncException) : IPlexMovieSyncService
    {
        public Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            if (syncException is not null)
            {
                return Task.FromException<PlexMovieSyncResultDto>(syncException);
            }

            PlexMovieSyncResultDto result = new(
                "completed",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-05T12:00:01Z"),
                1,
                500,
                500,
                0,
                40,
                220,
                3);

            return Task.FromResult(result);
        }
    }

    private sealed class SeededPlexMovieInventoryRepository : IPlexMovieInventoryRepository
    {
        private static readonly IReadOnlyList<PlexMovieDto> UnmatchedMovies =
        [
            new PlexMovieDto(
                "bond-1",
                "Dr. No",
                1962,
                "1",
                "Filmy",
                "plex://movie/bond-1",
                "tt0055928",
                646,
                null,
                DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
                "James Bond is sent to Jamaica to investigate the disappearance of a fellow agent.",
                "/library/metadata/bond-1/thumb/1",
                "/library/metadata/bond-1/art/1")
        ];

        public Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
            IReadOnlyList<PlexMovieDto> movies,
            IReadOnlySet<string> scannedSectionKeys,
            DateTimeOffset syncTime,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlexInventoryApplyResult(movies.Count, 0));
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(UnmatchedMovies);
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetUnmatchedMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(UnmatchedMovies);
        }

        public Task<PlexMovieDto?> GetMovieAsync(string ratingKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(UnmatchedMovies.FirstOrDefault(movie => movie.RatingKey == ratingKey));
        }

        public Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItemWriteModel>>([]);
        }

        public Task ApplyMatchUpdatesAsync(
            IReadOnlyList<PlexMovieMatchUpdate> updates,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SeededAvailabilityRefreshService(Exception? syncException) : IAvailabilityRefreshService
    {
        public Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken)
        {
            if (syncException is not null)
            {
                return Task.FromException<AvailabilityRefreshResultDto>(syncException);
            }

            AvailabilityRefreshResultDto result = new(
                "completed",
                true,
                "stale",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-05T12:00:05Z"),
                new PlexMovieSyncResultDto(
                    "completed",
                    DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                    DateTimeOffset.Parse("2026-06-05T12:00:05Z"),
                    1,
                    500,
                    500,
                    2,
                    40,
                    220,
                    3));

            return Task.FromResult(result);
        }
    }

    private sealed class SeededTmdbTvWatchlistSyncService(Exception? syncException) : ITmdbTvWatchlistSyncService
    {
        public Task<TmdbTvSyncResultDto> SyncAsync(CancellationToken cancellationToken)
        {
            if (syncException is not null)
            {
                return Task.FromException<TmdbTvSyncResultDto>(syncException);
            }

            TmdbTvSyncResultDto result = new(
                "completed",
                DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-06T12:00:02Z"),
                2,
                2,
                0,
                2,
                0,
                0);

            return Task.FromResult(result);
        }
    }

    private sealed class SeededWatchlistExportRepository : IWatchlistExportRepository
    {
        public Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<WatchlistExportMovieModel> movies =
            [
                new WatchlistExportMovieModel(
                    "1297842",
                    "tt27613895",
                    "GOAT",
                    2026,
                    "/film/goat-2026/",
                    [],
                    1297842,
                    "enriched",
                    Watchlist.Domain.AvailabilityStatus.NotOnPlex),
                new WatchlistExportMovieModel(
                    "4951",
                    "tt0147800",
                    "10 Things I Hate About You",
                    1999,
                    "/film/10-things-i-hate-about-you/",
                    ["Amazon Prime Video"],
                    4951,
                    "enriched",
                    Watchlist.Domain.AvailabilityStatus.AvailableOnPlex)
            ];

            return Task.FromResult(movies);
        }
    }

    private sealed class SeededCombinedSyncService(Exception? syncException) : ICombinedSyncService
    {
        public Task<CombinedSyncResultDto> SyncAllAsync(CancellationToken cancellationToken)
        {
            if (syncException is not null)
            {
                return Task.FromException<CombinedSyncResultDto>(syncException);
            }

            CombinedSyncResultDto result = new(
                "completed",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-05T12:00:04Z"),
                new LetterboxdSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:00Z"), DateTimeOffset.Parse("2026-06-05T12:00:01Z"), 2, 2, 0, "letterboxd-snapshot"),
                new TmdbMovieEnrichmentResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:01Z"), DateTimeOffset.Parse("2026-06-05T12:00:02Z"), 2, 2, 0, 0),
                new TmdbTvSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:02Z"), DateTimeOffset.Parse("2026-06-05T12:00:03Z"), 14, 14, 0, 14, 0, 0),
                new PlexMovieSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:03Z"), DateTimeOffset.Parse("2026-06-05T12:00:04Z"), 1, 500, 500, 0, 40, 220, 3));

            return Task.FromResult(result);
        }
    }
}
