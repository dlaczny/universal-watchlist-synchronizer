using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvSpecialEpisodeIdentityDocument
{
    [BsonElement("traktEpisodeId")]
    public long TraktEpisodeId { get; init; }

    [BsonElement("tvdbId")]
    public int? TvdbId { get; init; }

    [BsonElement("seasonNumber")]
    public int SeasonNumber { get; init; }

    [BsonElement("episodeNumber")]
    public int EpisodeNumber { get; init; }

    public static MongoTvSpecialEpisodeIdentityDocument FromDomain(
        TvSpecialEpisodeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new MongoTvSpecialEpisodeIdentityDocument
        {
            TraktEpisodeId = identity.TraktEpisodeId,
            TvdbId = identity.TvdbId,
            SeasonNumber = identity.SeasonNumber,
            EpisodeNumber = identity.EpisodeNumber
        };
    }

    public TvSpecialEpisodeIdentity ToDomain()
    {
        return new TvSpecialEpisodeIdentity(
            TraktEpisodeId,
            TvdbId,
            SeasonNumber,
            EpisodeNumber);
    }
}
