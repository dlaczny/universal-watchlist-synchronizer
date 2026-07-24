namespace Watchlist.Application;

public sealed record WorkerTvSnapshotDto(
    string SchemaVersion,
    string GenerationId,
    DateTimeOffset PublishedAt,
    DateTimeOffset GeneratedAt,
    string Kind,
    bool MutationCapable,
    WorkerTvDestinationSyncDto DestinationSync,
    IReadOnlyList<string> HealthReasons,
    WorkerTvPlexHistoryDto PlexHistory,
    IReadOnlyList<WorkerTvShowDto> Shows,
    IReadOnlyList<WorkerTvCleanupAuthorizationDto> CleanupAuthorizations);
