namespace Watchlist.Application;

/// <summary>
/// Contains the Trakt metadata needed to assemble a TV source row.
/// </summary>
public sealed record TraktShowMetadata(
    TraktShowIds Ids,
    string Title,
    int? Year,
    string? Overview,
    string? Status);
