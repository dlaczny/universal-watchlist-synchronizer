using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class SeededApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWatchlistReadRepository>();
            services.RemoveAll<ISyncStatusReadRepository>();
            RemoveBootstrapHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();
            services.AddSingleton<ISyncStatusReadRepository, SeededSyncStatusReadRepository>();
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
    }
}
