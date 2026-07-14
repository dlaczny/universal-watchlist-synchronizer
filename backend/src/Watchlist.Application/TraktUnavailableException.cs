namespace Watchlist.Application;

/// <summary>
/// Indicates that Trakt could not complete an operation transiently.
/// </summary>
public sealed class TraktUnavailableException()
    : Exception("Trakt is temporarily unavailable.");
