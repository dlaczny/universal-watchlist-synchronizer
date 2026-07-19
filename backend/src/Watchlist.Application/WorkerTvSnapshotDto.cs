namespace Watchlist.Application;

public sealed record WorkerTvSnapshotDto(
    string SchemaVersion,
    string GenerationId,
    DateTimeOffset PublishedAt,
    DateTimeOffset GeneratedAt,
    string Kind,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons,
    WorkerTvPlexHistoryDto PlexHistory,
    IReadOnlyList<WorkerTvShowDto> Shows,
    IReadOnlyList<WorkerTvCleanupAuthorizationDto> CleanupAuthorizations);
