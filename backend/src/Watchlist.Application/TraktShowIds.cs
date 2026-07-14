namespace Watchlist.Application;

/// <summary>
/// Contains the normalized identities for a Trakt show.
/// </summary>
public sealed record TraktShowIds(
    long TraktId,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId);
