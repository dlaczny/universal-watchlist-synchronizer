using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWatchlistInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoDbOptions>(configuration.GetSection(MongoDbOptions.SectionName));
        services.AddOptions<LetterboxdOptions>()
            .Bind(configuration.GetSection(LetterboxdOptions.SectionName))
            .Validate(IsValidWatchlistUrl, "Letterboxd:WatchlistUrl must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();
        IConfigurationSection tmdbSection = configuration.GetSection(TmdbOptions.SectionName);
        services.AddOptions<TmdbOptions>()
            .Bind(tmdbSection)
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be absolute.")
            .Validate(options => Uri.TryCreate(options.ImageBaseUrl, UriKind.Absolute, out _), "Tmdb:ImageBaseUrl must be absolute.");
        services.PostConfigure<TmdbOptions>(options =>
        {
            IConfigurationSection providerIdsSection = tmdbSection.GetSection(nameof(TmdbOptions.OwnedProviderIds));
            if (providerIdsSection.GetChildren().Any())
            {
                int[] configuredProviderIds = providerIdsSection.Get<int[]>() ?? [];
                options.OwnedProviderIds = configuredProviderIds;
            }
        });
        services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            MongoDbOptions options = serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });
        services.AddSingleton(serviceProvider =>
        {
            MongoDbOptions options = serviceProvider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
            IMongoClient client = serviceProvider.GetRequiredService<IMongoClient>();
            return client.GetDatabase(options.DatabaseName);
        });
        services.AddSingleton<IWatchlistReadRepository, MongoWatchlistReadRepository>();
        services.AddSingleton<IWatchlistExportRepository, MongoWatchlistExportRepository>();
        services.AddSingleton<IWatchlistWriteRepository, MongoWatchlistWriteRepository>();
        services.AddSingleton<ILetterboxdSourceSnapshotRepository, MongoLetterboxdSourceSnapshotRepository>();
        services.AddSingleton<ITmdbMovieMetadataRepository, MongoTmdbMovieMetadataRepository>();
        services.AddSingleton<ISyncStatusReadRepository, MongoSyncStatusReadRepository>();
        services.AddOptions<PlexOptions>()
            .Bind(configuration.GetSection(PlexOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.BaseUrl)
                || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Plex:BaseUrl must be absolute.")
            .ValidateOnStart();

        services.AddHttpClient<IPlexLibraryClient, PlexLibraryClient>((serviceProvider, httpClient) =>
        {
            PlexOptions options = serviceProvider.GetRequiredService<IOptions<PlexOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                httpClient.BaseAddress = new Uri(options.BaseUrl);
            }
        });
        services.AddSingleton<IPlexMovieInventoryRepository, MongoPlexMovieInventoryRepository>();
        services.AddScoped<IPlexMovieSyncService, PlexMovieSyncService>();
        services.AddScoped<IMovieSyncService, MovieSyncService>();
        services.AddScoped<IAvailabilityRefreshService, AvailabilityRefreshService>();
        services.AddScoped<ICombinedSyncService, CombinedSyncService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LetterboxdSyncGate>();
        services.AddSingleton<IHttpRetryDelay, DefaultHttpRetryDelay>();
        services.AddHttpClient<ILetterboxdWatchlistClient, LetterboxdWatchlistClient>();
        services.AddHttpClient<ITmdbMovieClient, TmdbMovieClient>((serviceProvider, httpClient) =>
        {
            TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddScoped<ILetterboxdMovieSyncService, LetterboxdMovieSyncService>();
        services.AddScoped<ITmdbMovieEnrichmentService, TmdbMovieEnrichmentService>();
        services.AddHttpClient<ITmdbTvWatchlistClient, TmdbTvWatchlistClient>((serviceProvider, httpClient) =>
        {
            TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddHttpClient<ITmdbTvMetadataClient, TmdbTvMetadataClient>((serviceProvider, httpClient) =>
        {
            TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddScoped<ITmdbTvWatchlistSyncService, TmdbTvWatchlistSyncService>();
        services.AddHostedService<MongoBootstrapHostedService>();

        return services;
    }

    private static bool IsValidWatchlistUrl(LetterboxdOptions options)
    {
        if (!Uri.TryCreate(options.WatchlistUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
