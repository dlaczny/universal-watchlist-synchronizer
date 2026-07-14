namespace Watchlist.Application;

/// <summary>
/// Indicates that a stored Trakt connection can no longer be decrypted.
/// </summary>
public sealed class TraktConnectionUnreadableException()
    : Exception("The stored Trakt connection cannot be decrypted.");
