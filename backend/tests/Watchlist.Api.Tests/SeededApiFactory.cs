using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchlist.Application;
using Watchlist.Domain;
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
    string? syncApiKey = null,
    Exception? traktStartException = null,
    Action? tmdbTvSyncInvoked = null,
    Exception? tvSyncException = null,
    List<string>? capturedLogs = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (syncApiKey is not null)
        {
            builder.UseSetting("Sync:ApiKey", syncApiKey);
        }

        builder.ConfigureServices(services =>
        {
            if (capturedLogs is not null)
            {
                services.AddSingleton<ILoggerProvider>(new CapturingLoggerProvider(capturedLogs));
            }

            services.RemoveAll<IWatchlistReadRepository>();
            services.RemoveAll<ITvShowReadRepository>();
            services.RemoveAll<ISyncStatusReadRepository>();
            services.RemoveAll<ILetterboxdMovieSyncService>();
            services.RemoveAll<ITmdbMovieEnrichmentService>();
            services.RemoveAll<IPlexMovieSyncService>();
            services.RemoveAll<IPlexMovieInventoryRepository>();
            services.RemoveAll<ICombinedSyncService>();
            services.RemoveAll<ITvSyncService>();
            services.RemoveAll<ITvGenerationRepository>();
            services.RemoveAll<ITvExportService>();
            services.RemoveAll<ITvStatusService>();
            services.RemoveAll<ITmdbTvWatchlistSyncService>();
            services.RemoveAll<IAvailabilityRefreshService>();
            services.RemoveAll<IWatchlistExportRepository>();
            services.RemoveAll<TraktConnectionService>();
            services.RemoveAll<ITraktConnectionService>();
            services.RemoveAll<ITraktAccessTokenProvider>();
            RemoveBootstrapHostedService(services);
            RemoveDataProtectionKeyRingHostedService(services);
            RemoveTraktHostedService(services);
            RemoveLegacyTvMigrationHostedService(services);
            RemoveTvIndexHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();
            services.AddSingleton<ITvShowReadRepository, SeededTvShowReadRepository>();
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
            services.AddSingleton<ITvSyncService>(_ => new SeededTvSyncService(tvSyncException));
            services.AddSingleton<ITvGenerationRepository, SeededTvGenerationRepository>();
            services.AddSingleton<ITvExportService, TvExportService>();
            services.AddSingleton<ITvStatusService, TvStatusService>();
            services.AddSingleton<ITmdbTvWatchlistSyncService>(
                _ => new SeededTmdbTvWatchlistSyncService(
                    tmdbTvSyncException,
                    tmdbTvSyncInvoked));
            services.AddSingleton<IAvailabilityRefreshService>(
                _ => new SeededAvailabilityRefreshService(availabilityRefreshException));
            services.AddSingleton<IWatchlistExportRepository, SeededWatchlistExportRepository>();
            services.AddSingleton<SeededTraktConnectionService>(
                _ => new SeededTraktConnectionService(traktStartException));
            services.AddSingleton<ITraktConnectionService>(serviceProvider =>
                serviceProvider.GetRequiredService<SeededTraktConnectionService>());
            services.AddSingleton<ITraktAccessTokenProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<SeededTraktConnectionService>());
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

    internal static void RemoveDataProtectionKeyRingHostedService(IServiceCollection services)
    {
        ServiceDescriptor? keyRingDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(DataProtectionKeyRingHostedService));

        if (keyRingDescriptor is not null)
        {
            services.Remove(keyRingDescriptor);
        }
    }

    private static void RemoveTraktHostedService(IServiceCollection services)
    {
        ServiceDescriptor? traktDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(TraktDeviceAuthorizationHostedService));

        if (traktDescriptor is not null)
        {
            services.Remove(traktDescriptor);
        }
    }

    internal static void RemoveLegacyTvMigrationHostedService(IServiceCollection services)
    {
        ServiceDescriptor? migrationDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(LegacyTvMigrationHostedService));

        if (migrationDescriptor is not null)
        {
            services.Remove(migrationDescriptor);
        }
    }

    private sealed class SeededTvShowReadRepository : ITvShowReadRepository
    {
        private static readonly TvProviderAvailability Availability = new(
            TvProviderState.Available,
            "PL",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            "https://www.themoviedb.org/tv/456/watch?locale=PL",
            [new TvProviderOffer(8, "Netflix", TvProviderCategory.Flatrate, "https://image.tmdb.org/t/p/w500/logo%2fprivate.png")]);

        private static readonly TvEpisodeProgress Episode = new(
            501,
            601,
            1,
            1,
            "Pilot",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            null);

        private static readonly IReadOnlyList<TvShow> Shows =
        [
            CreateShow("tv-trakt-12345", 12345, TvLifecycleState.Active),
            CreateShow("tv-trakt-12346", 12346, TvLifecycleState.CaughtUp),
            CreateShow("tv-trakt-12347", 12347, TvLifecycleState.RetiredTerminal)
        ];

        internal static readonly PublishedTvGeneration Generation = new(
            new TvGenerationManifest(
                "seeded-tv-generation",
                null,
                TvGenerationKind.ScheduledFull,
                DateTimeOffset.Parse("2026-07-19T11:59:00Z"),
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                new TraktActivityCursor(DateTimeOffset.Parse("2026-07-19T11:00:00Z"), DateTimeOffset.Parse("2026-07-19T11:00:00Z")),
                1,
                3,
                1,
                3,
                "v1",
                new Dictionary<string, string>(),
                "membership",
                "progress",
                null,
                null,
                null,
                "valid",
                [],
                [],
                [],
                false,
                ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"],
                []),
            Shows);

        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<PublishedTvGeneration?>(Generation);
        }

        public Task<TvShow?> GetPublishedShowAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Shows.SingleOrDefault(show => show.Id == id));
        }

        private static TvShow CreateShow(string id, long traktId, TvLifecycleState lifecycleState)
        {
            return new TvShow(
                id,
                traktId,
                123,
                456,
                "tt1234567",
                TvIdentityStatus.Verified,
                "Seeded TV Show",
                2026,
                "A seeded TV overview.",
                "https://image.tmdb.org/t/p/w500/poster.png",
                "https://image.tmdb.org/t/p/w1280/backdrop.png",
                "returning series",
                lifecycleState != TvLifecycleState.RetiredTerminal,
                1,
                0,
                null,
                Episode,
                [new TvSeasonProgress(1, 1, 0, false, Availability, [Episode])],
                [],
                Availability,
                lifecycleState,
                lifecycleState == TvLifecycleState.Active ? null : "lifecycle_event",
                1,
                0,
                DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T00:00:00Z"),
                "seeded-tv-generation",
                null);
        }
    }

    private sealed class SeededTvGenerationRepository : ITvGenerationRepository
    {
        public Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<PublishedTvGeneration?>(SeededTvShowReadRepository.Generation);
        }
    }

    private sealed class SeededTvSyncService(Exception? exception) : ITvSyncService
    {
        public Task<TvSyncResultDto> SyncAsync(TvGenerationKind kind, CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                return Task.FromException<TvSyncResultDto>(exception);
            }

            return Task.FromResult(new TvSyncResultDto(
                "completed",
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T12:00:01Z"),
                "seeded-tv-generation",
                "scheduled_full",
                3,
                3,
                3,
                0,
                false,
                ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"]));
        }
    }

    private sealed class SeededTraktConnectionService(Exception? startException)
        : ITraktConnectionService, ITraktAccessTokenProvider
    {
        public Task<TraktDeviceStartDto> StartDeviceAsync(CancellationToken cancellationToken)
        {
            if (startException is not null)
            {
                return Task.FromException<TraktDeviceStartDto>(startException);
            }

            return Task.FromResult(new TraktDeviceStartDto(
                "ABCD1234",
                "https://trakt.tv/activate",
                DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
                5));
        }

        public Task<TraktConnectionStatusDto> PollPendingAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TraktConnectionStatusDto(
                "connected",
                DateTimeOffset.Parse("2026-07-14T10:00:00Z"),
                DateTimeOffset.Parse("2026-10-14T10:00:00Z"),
                null));
        }

        public Task<TraktConnectionStatusDto> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TraktConnectionStatusDto(
                "connected",
                DateTimeOffset.Parse("2026-07-14T10:00:00Z"),
                DateTimeOffset.Parse("2026-10-14T10:00:00Z"),
                null));
        }

        public Task<TraktConnectionStatusDto> DisconnectAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TraktConnectionStatusDto(
                "disconnected",
                null,
                null,
                null));
        }

        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("seeded-access-token");
        }

        public Task<string> ForceRefreshAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("seeded-access-token");
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

    private sealed class SeededTmdbTvWatchlistSyncService(
        Exception? syncException,
        Action? invoked) : ITmdbTvWatchlistSyncService
    {
        public Task<TmdbTvSyncResultDto> SyncAsync(CancellationToken cancellationToken)
        {
            invoked?.Invoke();
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
        public Task<WatchlistMovieLifecycleExport> GetMovieLifecycleAsync(
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

            IReadOnlyList<WatchlistWatchedMovieModel> watchedMovies = [
                new WatchlistWatchedMovieModel(
                    202,
                    "tt0000202",
                    "Watched Movie",
                    2024,
                    "202",
                    DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                    1,
                    "movie-202:watched:1")
            ];
            LetterboxdSourceSnapshot snapshot = new(
                "letterboxd-snapshot",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                new HashSet<string>(["1297842", "4951"], StringComparer.Ordinal),
                [new PublishedWatchedMovie(
                    "202",
                    "movie-202:watched:1",
                    DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                    1)]);

            return Task.FromResult(new WatchlistMovieLifecycleExport(
                snapshot,
                movies,
                watchedMovies));
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
                "partial",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-05T12:00:04Z"),
                new LetterboxdSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:00Z"), DateTimeOffset.Parse("2026-06-05T12:00:01Z"), 2, 2, 0, "letterboxd-snapshot"),
                new TmdbMovieEnrichmentResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:01Z"), DateTimeOffset.Parse("2026-06-05T12:00:02Z"), 2, 2, 0, 0),
                new TvSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:04Z"), DateTimeOffset.Parse("2026-06-05T12:00:04Z"), "seeded-tv-generation", "scheduled_full", 3, 3, 3, 0, false, ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"]),
                new PlexMovieSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:03Z"), DateTimeOffset.Parse("2026-06-05T12:00:04Z"), 1, 500, 500, 0, 40, 220, 3));

            return Task.FromResult(result);
        }
    }

    internal static void RemoveTvIndexHostedService(IServiceCollection services)
    {
        ServiceDescriptor? indexDescriptor = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(MongoTvIndexHostedService));

        if (indexDescriptor is not null)
        {
            services.Remove(indexDescriptor);
        }
    }

    private sealed class CapturingLoggerProvider(List<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add(formatter(state, exception));
                }
            }
        }
    }
}
