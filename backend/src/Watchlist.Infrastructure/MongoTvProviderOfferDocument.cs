using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvProviderOfferDocument
{
    [BsonElement("providerId")]
    public int ProviderId { get; init; }

    [BsonElement("providerName")]
    public string ProviderName { get; init; } = string.Empty;

    [BsonElement("category")]
    public TvProviderCategory Category { get; init; }

    [BsonElement("logoUrl")]
    public string? LogoUrl { get; init; }

    public static MongoTvProviderOfferDocument FromDomain(TvProviderOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new MongoTvProviderOfferDocument
        {
            ProviderId = offer.ProviderId,
            ProviderName = offer.ProviderName,
            Category = offer.Category,
            LogoUrl = offer.LogoUrl
        };
    }

    public TvProviderOffer ToDomain()
    {
        return new TvProviderOffer(ProviderId, ProviderName, Category, LogoUrl);
    }
}
