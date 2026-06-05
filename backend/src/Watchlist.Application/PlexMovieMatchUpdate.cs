using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record PlexMovieMatchUpdate(
    string WatchlistItemId,
    AvailabilityStatus AvailabilityStatus,
    string? PlexRatingKey,
    DateTimeOffset? PlexMatchedAt,
    string PlexMatchReason,
    string PlexMatchConfidence);
