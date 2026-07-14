namespace Watchlist.Application;

/// <summary>
/// Represents one show from Trakt's complete watched-progress catalog.
/// </summary>
public sealed record TraktWatchedShowProgress(
    TraktShowIds Ids,
    string Title,
    int? Year,
    int AiredEpisodes,
    int CompletedEpisodes,
    TraktSeasonEpisode? NextEpisode,
    TraktSeasonEpisode? LastEpisode);
