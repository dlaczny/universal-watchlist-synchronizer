namespace Watchlist.Application;

public sealed record AvailabilityRefreshResultDto(
    string Status,
    bool RanPlexSync,
    string Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    PlexMovieSyncResultDto? Plex);
