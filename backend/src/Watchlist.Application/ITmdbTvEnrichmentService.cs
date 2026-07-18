using Watchlist.Domain;

namespace Watchlist.Application;

public interface ITmdbTvEnrichmentService
{
    Task<TmdbTvEnrichmentResult> EnrichAsync(
        TraktShowMetadata current,
        IReadOnlyList<int> numberedSeasonNumbers,
        TvShow? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
