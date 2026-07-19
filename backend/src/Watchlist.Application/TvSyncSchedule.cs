using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Selects deterministic complete TV refresh work for one scheduler cycle.
/// </summary>
public static class TvSyncSchedule
{
    public static TvGenerationKind? Decide(
        TraktConnectionStatusDto connection,
        TvGenerationManifest? publishedManifest,
        TraktActivityCursor? currentActivity,
        DateTimeOffset now,
        TimeSpan fullSyncInterval)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.Equals(connection.Status, "connected", StringComparison.Ordinal))
        {
            return null;
        }

        if (now == default || now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The scheduler timestamp must be UTC.", nameof(now));
        }

        if (fullSyncInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(fullSyncInterval));
        }

        if (publishedManifest is null)
        {
            return TvGenerationKind.ScheduledFull;
        }

        if (publishedManifest.PublishedAt > now)
        {
            return null;
        }

        DateTimeOffset lastScheduledFullAt = publishedManifest.LastScheduledFullAt
            ?? publishedManifest.PublishedAt;
        if (lastScheduledFullAt > now)
        {
            return null;
        }

        TimeSpan scheduledAge = now - lastScheduledFullAt;
        if (scheduledAge >= fullSyncInterval)
        {
            return TvGenerationKind.ScheduledFull;
        }

        ArgumentNullException.ThrowIfNull(currentActivity);
        return currentActivity != publishedManifest.ActivityCursor
            ? TvGenerationKind.ActivityFull
            : null;
    }
}
