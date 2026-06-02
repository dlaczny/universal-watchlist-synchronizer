using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoSyncRunDocument
{
    [BsonId]
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? LastSuccessfulSyncAt { get; init; }

    public SyncStatusDto ToDto()
    {
        return new SyncStatusDto(Status, LastSuccessfulSyncAt);
    }
}
