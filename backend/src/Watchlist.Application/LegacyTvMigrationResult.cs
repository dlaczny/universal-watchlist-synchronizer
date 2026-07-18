namespace Watchlist.Application;

/// <summary>
/// Counts newly inserted legacy TV migration records by classification.
/// </summary>
public sealed record LegacyTvMigrationResult(
    int MigratedCount,
    int QuarantinedCount);
