namespace Watchlist.Application;

/// <summary>
/// Indicates that a critical Trakt connection state change could not be persisted in time.
/// </summary>
public sealed class TraktPersistenceUnavailableException()
    : Exception("Trakt connection persistence is temporarily unavailable.");
