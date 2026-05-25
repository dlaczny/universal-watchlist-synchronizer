using Microsoft.Extensions.DependencyInjection;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWatchlistInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();

        return services;
    }
}
