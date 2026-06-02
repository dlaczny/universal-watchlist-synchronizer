using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Api.Tests;

public sealed class MongoUnavailableApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWatchlistReadRepository>();
            SeededApiFactory.RemoveBootstrapHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, ThrowingWatchlistReadRepository>();
        });
    }

    private sealed class ThrowingWatchlistReadRepository : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            throw new MongoClientException("MongoDB is unavailable.");
        }
    }
}
