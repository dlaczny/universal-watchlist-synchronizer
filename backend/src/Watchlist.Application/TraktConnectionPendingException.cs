namespace Watchlist.Application;

/// <summary>
/// Indicates that an unexpired Trakt device authorization is already pending.
/// </summary>
public sealed class TraktConnectionPendingException()
    : Exception("A Trakt device authorization is already pending.");
