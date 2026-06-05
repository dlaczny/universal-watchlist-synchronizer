using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Write model that carries a normalized domain item plus source trace fields.
/// </summary>
public sealed record WatchlistItemWriteModel(
    WatchlistItem Item,
    string? ImdbId,
    string? LetterboxdPath,
    int? TmdbId = null);
