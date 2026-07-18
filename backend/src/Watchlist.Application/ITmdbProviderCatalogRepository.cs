namespace Watchlist.Application;

public interface ITmdbProviderCatalogRepository
{
    Task<TmdbProviderCatalogSnapshot?> GetAsync(CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetLastAttemptAtAsync(CancellationToken cancellationToken);

    Task ReplaceAsync(
        TmdbProviderCatalogSnapshot snapshot,
        CancellationToken cancellationToken);

    Task MarkStaleAsync(
        string errorCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken);
}
