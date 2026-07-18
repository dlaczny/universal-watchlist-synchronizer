namespace Watchlist.Application;

/// <summary>
/// Reports a publication-critical TV source invariant failure.
/// </summary>
public sealed class TvSourceSnapshotRejectedException(string reason) : Exception(reason);
