using Microsoft.AspNetCore.DataProtection;
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
        IConfigurationSection dataProtectionSection = configuration.GetSection(
            DataProtectionKeyRingOptions.SectionName);
        services.Configure<DataProtectionKeyRingOptions>(dataProtectionSection);
        DataProtectionKeyRingOptions keyRing =
            dataProtectionSection.Get<DataProtectionKeyRingOptions>() ?? new();
        services.AddDataProtection()
            .SetApplicationName(keyRing.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing.KeyRingPath));
        services.AddOptions<LetterboxdOptions>()
            .Bind(configuration.GetSection(LetterboxdOptions.SectionName))
            .Validate(IsValidWatchlistUrl, "Letterboxd:WatchlistUrl must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();
        IConfigurationSection tmdbSection = configuration.GetSection(TmdbOptions.SectionName);
        services.AddOptions<TmdbOptions>()
            .Bind(tmdbSection)
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be absolute.")
            .Validate(options => Uri.TryCreate(options.ImageBaseUrl, UriKind.Absolute, out _), "Tmdb:ImageBaseUrl must be absolute.")
            .Validate(
                options => options.ProviderRegion.Length == 2
                    && options.ProviderRegion.All(character => character is >= 'A' and <= 'Z'),
                "Tmdb:ProviderRegion must be an uppercase ISO alpha-2 code.")
            .Validate(
                options => options.OwnedProviderIds.Count > 0
                    && options.OwnedProviderIds.All(providerId => providerId > 0)
                    && options.OwnedProviderIds.Distinct().Count() == options.OwnedProviderIds.Count,
                "Tmdb:OwnedProviderIds must contain unique positive IDs.")
            .Validate(
                options => options.ProviderCacheLifetime > TimeSpan.Zero,
                "Tmdb:ProviderCacheLifetime must be positive.")
            .ValidateOnStart();
        services.AddOptions<TraktOptions>()
            .Bind(configuration.GetSection(TraktOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Trakt:BaseUrl must be absolute.")
            .Validate(
                options => options.PageSize is >= 1 and <= TraktTvClient.MaximumPageSize,
                "Trakt:PageSize must be between 1 and 100.")
            .Validate(
                options => options.MetadataRefreshInterval > TimeSpan.Zero,
                "Trakt:MetadataRefreshInterval must be positive.")
            .ValidateOnStart();
        services.PostConfigure<TmdbOptions>(options =>
        {
            IConfigurationSection providerIdsSection = tmdbSection.GetSection(nameof(TmdbOptions.OwnedProviderIds));
            if (providerIdsSection.GetChildren().Any())
            {
                int[] configuredProviderIds = providerIdsSection.Get<int[]>() ?? [];
                options.OwnedProviderIds = configuredProviderIds;
            }
        });
        services.AddSingleton(serviceProvider =>
        {
            TmdbOptions tmdbOptions = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            TraktOptions traktOptions = serviceProvider.GetRequiredService<IOptions<TraktOptions>>().Value;
            return new TmdbEnrichmentSettings(
                tmdbOptions.ProviderRegion,
                tmdbOptions.OwnedProviderIds,
                traktOptions.MetadataRefreshInterval,
                tmdbOptions.ProviderCacheLifetime);
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
        services.AddSingleton<ITraktTokenProtector, DataProtectionTraktTokenProtector>();
        services.AddSingleton<ITraktConnectionRepository, MongoTraktConnectionRepository>();
        services.AddSingleton<ITmdbProviderCatalogRepository, MongoTmdbProviderCatalogRepository>();
        services.AddHttpClient(TraktOAuthClient.HttpClientName, (serviceProvider, httpClient) =>
        {
            TraktOptions options = serviceProvider.GetRequiredService<IOptions<TraktOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddSingleton<ITraktOAuthClient, TraktOAuthClient>();
        services.AddHttpClient(TraktTvClient.HttpClientName, (serviceProvider, httpClient) =>
        {
            TraktOptions options = serviceProvider.GetRequiredService<IOptions<TraktOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddSingleton<ITraktTvClient, TraktTvClient>();
        services.AddSingleton<TraktConnectionService>(serviceProvider =>
        {
            TraktOptions traktOptions = serviceProvider
                .GetRequiredService<IOptions<TraktOptions>>()
                .Value;
            return new TraktConnectionService(
                serviceProvider.GetRequiredService<ITraktConnectionRepository>(),
                serviceProvider.GetRequiredService<ITraktOAuthClient>(),
                serviceProvider.GetRequiredService<ITraktTokenProtector>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                traktOptions.TokenRefreshSkew);
        });
        services.AddSingleton<ITraktConnectionService>(serviceProvider =>
            serviceProvider.GetRequiredService<TraktConnectionService>());
        services.AddSingleton<ITraktAccessTokenProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TraktConnectionService>());
        services.AddSingleton<IWatchlistReadRepository, MongoWatchlistReadRepository>();
        services.AddSingleton<IWatchlistExportRepository, MongoWatchlistExportRepository>();
        services.AddSingleton<IWatchlistWriteRepository, MongoWatchlistWriteRepository>();
        services.AddSingleton<ITvGenerationRepository, MongoTvGenerationRepository>();
        services.AddSingleton<ITvShowReadRepository, MongoTvShowReadRepository>();
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
        services.AddHttpClient(TmdbTvMetadataClient.HttpClientName, (serviceProvider, httpClient) =>
        {
            TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddSingleton<ITmdbTvMetadataClient, TmdbTvMetadataClient>();
        services.AddSingleton<ITmdbTvEnrichmentService, TmdbTvEnrichmentService>();
        services.AddScoped<ITmdbTvWatchlistSyncService, TmdbTvWatchlistSyncService>();
        services.AddHostedService<DataProtectionKeyRingHostedService>();
        services.AddHostedService<TraktDeviceAuthorizationHostedService>();
        services.AddHostedService<MongoTvIndexHostedService>();
        services.AddHostedService<MongoBootstrapHostedService>();
        services.AddHostedService<TmdbProviderCatalogHostedService>();

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
