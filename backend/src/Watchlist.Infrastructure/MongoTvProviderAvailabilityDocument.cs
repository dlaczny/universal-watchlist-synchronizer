using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvProviderAvailabilityDocument
{
    [BsonElement("state")]
    public TvProviderState State { get; init; }

    [BsonElement("region")]
    public string Region { get; init; } = string.Empty;

    [BsonElement("fetchedAt")]
    public DateTimeOffset? FetchedAt { get; init; }

    [BsonElement("link")]
    public string? Link { get; init; }

    [BsonElement("offers")]
    public IReadOnlyList<MongoTvProviderOfferDocument> Offers { get; init; } = [];

    public static MongoTvProviderAvailabilityDocument FromDomain(
        TvProviderAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        return new MongoTvProviderAvailabilityDocument
        {
            State = availability.State,
            Region = availability.Region,
            FetchedAt = availability.FetchedAt,
            Link = availability.Link,
            Offers = availability.Offers
                .Select(MongoTvProviderOfferDocument.FromDomain)
                .ToArray()
        };
    }

    public TvProviderAvailability ToDomain()
    {
        ArgumentNullException.ThrowIfNull(Offers);
        if (Offers.Any(offer => offer is null))
        {
            throw new InvalidOperationException("tv_generation_provider_offer_invalid");
        }

        return new TvProviderAvailability(
            State,
            Region,
            FetchedAt,
            Link,
            Offers.Select(offer => offer.ToDomain()).ToArray());
    }
}
