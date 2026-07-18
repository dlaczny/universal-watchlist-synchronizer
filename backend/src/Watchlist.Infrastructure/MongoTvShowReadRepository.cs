using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvShowReadRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITvShowReadRepository
{
    private readonly MongoTvPublishedGenerationLoader loader =
        new(database, options.Value);

    public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
    {
        return loader.LoadAsync(cancellationToken);
    }

    public async Task<TvShow?> GetPublishedShowAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        PublishedTvGeneration? generation = await loader.LoadAsync(cancellationToken);
        return generation?.Shows.SingleOrDefault(
            show => string.Equals(show.Id, id, StringComparison.Ordinal));
    }
}
