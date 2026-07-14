namespace Watchlist.Domain;

public sealed record TvLifecycleEvent(
    string Id,
    long TraktId,
    long Version,
    string GenerationId,
    string EventType,
    DateTimeOffset OccurredAt,
    string PredicateHash,
    string Reason);
