using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Reads TV shows through exactly one published generation.
/// </summary>
public interface ITvShowReadRepository
{
    Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken);

    Task<TvShow?> GetPublishedShowAsync(string id, CancellationToken cancellationToken);
}
