namespace Watchlist.Application;

public interface ITmdbTvMetadataClient
{
    Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken);
}
