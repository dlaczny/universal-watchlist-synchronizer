namespace Watchlist.Application;

/// <summary>
/// Copies legacy TV rows into inert migration records without granting source authority.
/// </summary>
public interface ILegacyTvMigrationService
{
    /// <summary>
    /// Inserts any legacy TV migration records that do not already exist.
    /// </summary>
    Task<LegacyTvMigrationResult> MigrateAsync(CancellationToken cancellationToken);
}
