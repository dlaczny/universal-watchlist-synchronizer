namespace Watchlist.Application;

/// <summary>
/// Indicates that a successful Trakt response did not match its contract.
/// </summary>
public sealed class TraktParseException()
    : Exception("The Trakt response could not be parsed.");
