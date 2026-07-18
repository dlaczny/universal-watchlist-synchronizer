using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTmdbProviderCatalogDocument
{
    public const string SingletonId = "singleton";

    [BsonId]
    public string Id { get; init; } = SingletonId;

    [BsonElement("catalogFetchedAt")]
    public DateTimeOffset? CatalogFetchedAt { get; init; }

    [BsonElement("regionsFetchedAt")]
    public DateTimeOffset? RegionsFetchedAt { get; init; }

    [BsonElement("stale")]
    public bool Stale { get; init; }

    [BsonElement("lastErrorCode")]
    public string? LastErrorCode { get; init; }

    [BsonElement("lastErrorAt")]
    public DateTimeOffset? LastErrorAt { get; init; }

    [BsonElement("providers")]
    public IReadOnlyList<MongoTmdbProviderCatalogEntryDocument> Providers { get; init; } = [];

    [BsonElement("regionCodes")]
    public IReadOnlyList<string> RegionCodes { get; init; } = [];

    public static MongoTmdbProviderCatalogDocument FromSnapshot(
        TmdbProviderCatalogSnapshot snapshot)
    {
        return new MongoTmdbProviderCatalogDocument
        {
            CatalogFetchedAt = snapshot.CatalogFetchedAt,
            RegionsFetchedAt = snapshot.RegionsFetchedAt,
            Stale = snapshot.Stale,
            LastErrorCode = snapshot.LastErrorCode,
            LastErrorAt = snapshot.LastErrorAt,
            Providers = snapshot.Providers
                .Select(MongoTmdbProviderCatalogEntryDocument.FromDto)
                .ToArray(),
            RegionCodes = snapshot.RegionCodes.ToArray()
        };
    }

    public TmdbProviderCatalogSnapshot? ToSnapshot()
    {
        if (CatalogFetchedAt is null && RegionsFetchedAt is null)
        {
            return null;
        }

        if (CatalogFetchedAt is null || RegionsFetchedAt is null)
        {
            throw new TmdbParseException("TMDB provider catalog cache was invalid.");
        }

        try
        {
            return new TmdbProviderCatalogSnapshot(
                CatalogFetchedAt.Value.ToUniversalTime(),
                RegionsFetchedAt.Value.ToUniversalTime(),
                Stale,
                LastErrorCode,
                LastErrorAt?.ToUniversalTime(),
                Providers.Select(provider => provider.ToDto()).ToArray(),
                RegionCodes.ToArray());
        }
        catch (ArgumentException)
        {
            throw new TmdbParseException("TMDB provider catalog cache was invalid.");
        }
    }

    public DateTimeOffset? GetLastAttemptAt()
    {
        DateTimeOffset?[] values = [CatalogFetchedAt, RegionsFetchedAt, LastErrorAt];
        DateTimeOffset[] present = values
            .Where(value => value is not null)
            .Select(value => value!.Value.ToUniversalTime())
            .ToArray();
        return present.Length == 0 ? null : present.Max();
    }
}
