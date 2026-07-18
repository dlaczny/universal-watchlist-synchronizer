using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Describes the reduced Phase 1 lifecycle state for one TV show.
/// </summary>
public sealed record TvLifecycleDecision(
    TvLifecycleState State,
    long LifecycleVersion,
    int MissingScheduledConfirmations,
    TvLifecycleEvent? Event);
