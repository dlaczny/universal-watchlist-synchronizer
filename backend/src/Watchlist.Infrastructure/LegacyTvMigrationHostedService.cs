using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class LegacyTvMigrationHostedService(
    ILegacyTvMigrationService migrationService,
    ILogger<LegacyTvMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LegacyTvMigrationResult result = await migrationService
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Legacy TV migration completed with {MigratedCount} migrated and {QuarantinedCount} quarantined rows.",
            result.MigratedCount,
            result.QuarantinedCount);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
