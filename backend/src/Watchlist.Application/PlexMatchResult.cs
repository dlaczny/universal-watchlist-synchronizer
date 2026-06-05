using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record PlexMatchResult(
    AvailabilityStatus AvailabilityStatus,
    string? PlexRatingKey,
    string MatchReason,
    string MatchConfidence);
