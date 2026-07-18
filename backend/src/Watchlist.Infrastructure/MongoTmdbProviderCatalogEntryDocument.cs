using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTmdbProviderCatalogEntryDocument
{
    [BsonElement("providerId")]
    public int ProviderId { get; init; }

    [BsonElement("providerName")]
    public string ProviderName { get; init; } = string.Empty;

    [BsonElement("logoPath")]
    public string? LogoPath { get; init; }

    [BsonElement("displayPriority")]
    public int DisplayPriority { get; init; }

    public static MongoTmdbProviderCatalogEntryDocument FromDto(
        TmdbWatchProviderCatalogEntryDto provider)
    {
        return new MongoTmdbProviderCatalogEntryDocument
        {
            ProviderId = provider.ProviderId,
            ProviderName = provider.ProviderName,
            LogoPath = provider.LogoPath,
            DisplayPriority = provider.DisplayPriority
        };
    }

    public TmdbWatchProviderCatalogEntryDto ToDto()
    {
        return new TmdbWatchProviderCatalogEntryDto(
            ProviderId,
            ProviderName,
            LogoPath,
            DisplayPriority);
    }
}
