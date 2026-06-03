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
        services.Configure<LetterboxdOptions>(configuration.GetSection(LetterboxdOptions.SectionName));
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
        services.AddSingleton<ISyncStatusReadRepository, MongoSyncStatusReadRepository>();
        services.AddHttpClient<ILetterboxdWatchlistClient, LetterboxdWatchlistClient>();
        services.AddHostedService<MongoBootstrapHostedService>();

        return services;
    }
}
