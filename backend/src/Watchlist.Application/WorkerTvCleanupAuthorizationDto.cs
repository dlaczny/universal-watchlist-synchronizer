namespace Watchlist.Application;

public sealed record WorkerTvCleanupAuthorizationDto(
    string EventId,
    string ActionType,
    long TraktId,
    int TvdbId,
    int? SeasonNumber,
    long LifecycleVersion,
    string PredicateHash,
    string ManifestId,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset ExpiresAt,
    int ExpectedAired,
    int ExpectedCompleted,
    DateTimeOffset PlexEvidenceCollectedAt,
    DateTimeOffset? PlexHistoryWatermark);
