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
    private readonly string testKeyRingPath = SeededApiFactory.CreateTestKeyRingPath();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        SeededApiFactory.ConfigureTestHost(builder, testKeyRingPath);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWatchlistReadRepository>();
            services.RemoveAll<IWatchlistExportRepository>();
            SeededApiFactory.RemoveBootstrapHostedService(services);
            SeededApiFactory.RemoveLegacyTvMigrationHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, ThrowingWatchlistReadRepository>();
            services.AddSingleton<IWatchlistExportRepository, ThrowingWatchlistExportRepository>();
        });
    }

    private sealed class ThrowingWatchlistReadRepository : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            throw new MongoClientException("MongoDB is unavailable.");
        }
    }

    private sealed class ThrowingWatchlistExportRepository : IWatchlistExportRepository
    {
        public Task<WatchlistMovieLifecycleExport> GetMovieLifecycleAsync(
            CancellationToken cancellationToken)
        {
            throw new MongoClientException("MongoDB is unavailable.");
        }
    }
}
