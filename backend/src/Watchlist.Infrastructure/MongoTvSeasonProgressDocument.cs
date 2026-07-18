using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvSeasonProgressDocument
{
    [BsonElement("seasonNumber")]
    public int SeasonNumber { get; init; }

    [BsonElement("airedEpisodes")]
    public int AiredEpisodes { get; init; }

    [BsonElement("completedEpisodes")]
    public int CompletedEpisodes { get; init; }

    [BsonElement("hasKnownFutureEpisode")]
    public bool HasKnownFutureEpisode { get; init; }

    [BsonElement("availability")]
    public MongoTvProviderAvailabilityDocument? Availability { get; init; }

    [BsonElement("episodes")]
    public IReadOnlyList<MongoTvEpisodeProgressDocument> Episodes { get; init; } = [];

    public static MongoTvSeasonProgressDocument FromDomain(TvSeasonProgress season)
    {
        ArgumentNullException.ThrowIfNull(season);
        return new MongoTvSeasonProgressDocument
        {
            SeasonNumber = season.SeasonNumber,
            AiredEpisodes = season.AiredEpisodes,
            CompletedEpisodes = season.CompletedEpisodes,
            HasKnownFutureEpisode = season.HasKnownFutureEpisode,
            Availability = MongoTvProviderAvailabilityDocument.FromDomain(season.Availability),
            Episodes = season.Episodes
                .Select(MongoTvEpisodeProgressDocument.FromDomain)
                .ToArray()
        };
    }

    public TvSeasonProgress ToDomain()
    {
        ArgumentNullException.ThrowIfNull(Availability);
        ArgumentNullException.ThrowIfNull(Episodes);
        if (Episodes.Any(episode => episode is null))
        {
            throw new InvalidOperationException("tv_generation_episode_invalid");
        }

        return new TvSeasonProgress(
            SeasonNumber,
            AiredEpisodes,
            CompletedEpisodes,
            HasKnownFutureEpisode,
            Availability.ToDomain(),
            Episodes.Select(episode => episode.ToDomain()).ToArray());
    }
}
