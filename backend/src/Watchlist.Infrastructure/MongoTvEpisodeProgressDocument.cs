using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvEpisodeProgressDocument
{
    [BsonElement("traktEpisodeId")]
    public long TraktEpisodeId { get; init; }

    [BsonElement("tvdbId")]
    public int? TvdbId { get; init; }

    [BsonElement("seasonNumber")]
    public int SeasonNumber { get; init; }

    [BsonElement("episodeNumber")]
    public int EpisodeNumber { get; init; }

    [BsonElement("title")]
    public string? Title { get; init; }

    [BsonElement("airedAt")]
    public DateTimeOffset? AiredAt { get; init; }

    [BsonElement("watched")]
    public bool Watched { get; init; }

    [BsonElement("watchedAt")]
    public DateTimeOffset? WatchedAt { get; init; }

    public static MongoTvEpisodeProgressDocument FromDomain(TvEpisodeProgress episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new MongoTvEpisodeProgressDocument
        {
            TraktEpisodeId = episode.TraktEpisodeId,
            TvdbId = episode.TvdbId,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            AiredAt = episode.AiredAt,
            Watched = episode.Watched,
            WatchedAt = episode.WatchedAt
        };
    }

    public TvEpisodeProgress ToDomain()
    {
        return new TvEpisodeProgress(
            TraktEpisodeId,
            TvdbId,
            SeasonNumber,
            EpisodeNumber,
            Title,
            AiredAt,
            Watched,
            WatchedAt);
    }
}
