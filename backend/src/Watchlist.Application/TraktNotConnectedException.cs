namespace Watchlist.Application;

/// <summary>
/// Indicates that no usable Trakt access token is connected.
/// </summary>
public sealed class TraktNotConnectedException()
    : Exception("A usable Trakt connection is not available.");
