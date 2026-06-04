using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class SeededApiFactory(Exception? letterboxdSyncException = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWatchlistReadRepository>();
            services.RemoveAll<ISyncStatusReadRepository>();
            services.RemoveAll<ILetterboxdMovieSyncService>();
            RemoveBootstrapHostedService(services);
            services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();
            services.AddSingleton<ISyncStatusReadRepository, SeededSyncStatusReadRepository>();
            services.AddSingleton<ILetterboxdMovieSyncService>(
                _ => new SeededLetterboxdMovieSyncService(letterboxdSyncException));
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
                0);

            return Task.FromResult(result);
        }
    }
}
