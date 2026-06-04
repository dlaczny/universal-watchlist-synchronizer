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
        services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be absolute.")
            .Validate(options => Uri.TryCreate(options.ImageBaseUrl, UriKind.Absolute, out _), "Tmdb:ImageBaseUrl must be absolute.");
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
        services.AddSingleton<IWatchlistWriteRepository, MongoWatchlistWriteRepository>();
        services.AddSingleton<ITmdbMovieMetadataRepository, MongoTmdbMovieMetadataRepository>();
        services.AddSingleton<ISyncStatusReadRepository, MongoSyncStatusReadRepository>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<ILetterboxdWatchlistClient, LetterboxdWatchlistClient>();
        services.AddHttpClient<ITmdbMovieClient, TmdbMovieClient>((serviceProvider, httpClient) =>
        {
            TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddScoped<ILetterboxdMovieSyncService, LetterboxdMovieSyncService>();
        services.AddScoped<ITmdbMovieEnrichmentService, TmdbMovieEnrichmentService>();
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
