using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTraktConnectionRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITraktConnectionRepository
{
    private readonly IMongoCollection<MongoTraktConnectionDocument> connections =
        database.GetCollection<MongoTraktConnectionDocument>(
            options.Value.TraktConnectionsCollectionName);

    public async Task<TraktConnection?> GetAsync(CancellationToken cancellationToken)
    {
        MongoTraktConnectionDocument? document = await connections
            .Find(connection => connection.Id == MongoTraktConnectionDocument.SingletonId)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
    }

    public async Task SaveAsync(
        TraktConnection connection,
        CancellationToken cancellationToken)
    {
        MongoTraktConnectionDocument document =
            MongoTraktConnectionDocument.FromDomain(connection);
        await connections.ReplaceOneAsync(
            stored => stored.Id == MongoTraktConnectionDocument.SingletonId,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await connections.DeleteOneAsync(
            connection => connection.Id == MongoTraktConnectionDocument.SingletonId,
            cancellationToken);
    }
}
