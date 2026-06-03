using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Defines backend-owned watchlist browsing controls.
/// </summary>
public sealed record WatchlistQuery(
    WatchlistCollection Collection,
    IReadOnlySet<AvailabilityStatus> Availability,
    WatchlistSort Sort);
