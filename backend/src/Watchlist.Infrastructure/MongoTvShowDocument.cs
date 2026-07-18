using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvShowDocument
{
    public const string GenerationDocumentKind = "generation";
    public const string LegacyDocumentKind = "legacy";

    [BsonId]
    public string Id { get; init; } = string.Empty;

    [BsonElement("documentKind")]
    public string DocumentKind { get; init; } = string.Empty;

    [BsonElement("generationId")]
    [BsonIgnoreIfNull]
    public string? GenerationId { get; init; }

    [BsonElement("traktId")]
    [BsonIgnoreIfNull]
    public long? TraktId { get; init; }

    [BsonElement("publicId")]
    [BsonIgnoreIfNull]
    public string? PublicId { get; init; }

    [BsonElement("tvdbId")]
    public int? TvdbId { get; init; }

    [BsonElement("tmdbId")]
    public int? TmdbId { get; init; }

    [BsonElement("imdbId")]
    public string? ImdbId { get; init; }

    [BsonElement("identityStatus")]
    public TvIdentityStatus? IdentityStatus { get; init; }

    [BsonElement("title")]
    public string? Title { get; init; }

    [BsonElement("year")]
    public int? Year { get; init; }

    [BsonElement("overview")]
    public string? Overview { get; init; }

    [BsonElement("posterUrl")]
    public string? PosterUrl { get; init; }

    [BsonElement("backdropUrl")]
    public string? BackdropUrl { get; init; }

    [BsonElement("traktStatus")]
    [BsonIgnoreIfNull]
    public string? TraktStatus { get; init; }

    [BsonElement("inWatchlist")]
    [BsonIgnoreIfNull]
    public bool? InWatchlist { get; init; }

    [BsonElement("airedEpisodes")]
    [BsonIgnoreIfNull]
    public int? AiredEpisodes { get; init; }

    [BsonElement("completedEpisodes")]
    [BsonIgnoreIfNull]
    public int? CompletedEpisodes { get; init; }

    [BsonElement("lastWatchedEpisode")]
    [BsonIgnoreIfNull]
    public MongoTvEpisodeProgressDocument? LastWatchedEpisode { get; init; }

    [BsonElement("nextEpisode")]
    [BsonIgnoreIfNull]
    public MongoTvEpisodeProgressDocument? NextEpisode { get; init; }

    [BsonElement("seasons")]
    public IReadOnlyList<MongoTvSeasonProgressDocument> Seasons { get; init; } = [];

    [BsonElement("specialEpisodeIdentities")]
    public IReadOnlyList<MongoTvSpecialEpisodeIdentityDocument> SpecialEpisodeIdentities
    {
        get;
        init;
    } = [];

    [BsonElement("availability")]
    [BsonIgnoreIfNull]
    public MongoTvProviderAvailabilityDocument? Availability { get; init; }

    [BsonElement("lifecycleState")]
    [BsonIgnoreIfNull]
    public TvLifecycleState? LifecycleState { get; init; }

    [BsonElement("lastLifecycleEvent")]
    [BsonIgnoreIfNull]
    public string? LastLifecycleEvent { get; init; }

    [BsonElement("lifecycleVersion")]
    [BsonIgnoreIfNull]
    public long? LifecycleVersion { get; init; }

    [BsonElement("missingScheduledConfirmations")]
    [BsonIgnoreIfNull]
    public int? MissingScheduledConfirmations { get; init; }

    [BsonElement("addedAt")]
    public DateTimeOffset? AddedAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [BsonElement("metadataFetchedAt")]
    [BsonIgnoreIfNull]
    public DateTimeOffset? MetadataFetchedAt { get; init; }

    [BsonElement("legacySourceId")]
    public string? LegacySourceId { get; init; }

    [BsonElement("legacyWatchlistItemId")]
    public string? LegacyWatchlistItemId { get; init; }

    [BsonElement("legacyMigratedAt")]
    public DateTimeOffset? LegacyMigratedAt { get; init; }

    [BsonElement("legacyMigrationStatus")]
    public string? LegacyMigrationStatus { get; init; }

    [BsonElement("legacyMigrationReason")]
    public string? LegacyMigrationReason { get; init; }

    [BsonElement("genres")]
    public IReadOnlyList<string> Genres { get; init; } = [];

    [BsonElement("originalLanguage")]
    public string? OriginalLanguage { get; init; }

    [BsonElement("tmdbVoteAverage")]
    public double? TmdbVoteAverage { get; init; }

    [BsonElement("tmdbVoteCount")]
    public int? TmdbVoteCount { get; init; }

    public static MongoTvShowDocument FromDomain(TvShow show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return new MongoTvShowDocument
        {
            Id = FormattableString.Invariant(
                $"generation:{show.GenerationId}:{show.TraktId}"),
            DocumentKind = GenerationDocumentKind,
            GenerationId = show.GenerationId,
            TraktId = show.TraktId,
            PublicId = show.Id,
            TvdbId = show.TvdbId,
            TmdbId = show.TmdbId,
            ImdbId = show.ImdbId,
            IdentityStatus = show.IdentityStatus,
            Title = show.Title,
            Year = show.Year,
            Overview = show.Overview,
            PosterUrl = show.PosterUrl,
            BackdropUrl = show.BackdropUrl,
            TraktStatus = show.TraktStatus,
            InWatchlist = show.InWatchlist,
            AiredEpisodes = show.AiredEpisodes,
            CompletedEpisodes = show.CompletedEpisodes,
            LastWatchedEpisode = show.LastWatchedEpisode is null
                ? null
                : MongoTvEpisodeProgressDocument.FromDomain(show.LastWatchedEpisode),
            NextEpisode = show.NextEpisode is null
                ? null
                : MongoTvEpisodeProgressDocument.FromDomain(show.NextEpisode),
            Seasons = show.Seasons.Select(MongoTvSeasonProgressDocument.FromDomain).ToArray(),
            SpecialEpisodeIdentities = show.SpecialEpisodeIdentities
                .Select(MongoTvSpecialEpisodeIdentityDocument.FromDomain)
                .ToArray(),
            Availability = MongoTvProviderAvailabilityDocument.FromDomain(show.Availability),
            LifecycleState = show.LifecycleState,
            LastLifecycleEvent = show.LastLifecycleEvent,
            LifecycleVersion = show.LifecycleVersion,
            MissingScheduledConfirmations = show.MissingScheduledConfirmations,
            AddedAt = show.AddedAt,
            UpdatedAt = show.UpdatedAt,
            MetadataFetchedAt = show.MetadataFetchedAt,
            LegacySourceId = show.LegacySourceId,
            LegacyWatchlistItemId = null,
            LegacyMigratedAt = null,
            LegacyMigrationStatus = null,
            LegacyMigrationReason = null,
            Genres = [],
            OriginalLanguage = null,
            TmdbVoteAverage = null,
            TmdbVoteCount = null
        };
    }

    public TvShow ToDomain()
    {
        if (!string.Equals(DocumentKind, GenerationDocumentKind, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(GenerationId)
            || TraktId is not > 0
            || string.IsNullOrWhiteSpace(PublicId)
            || IdentityStatus is null
            || string.IsNullOrWhiteSpace(Title)
            || string.IsNullOrWhiteSpace(TraktStatus)
            || InWatchlist is null
            || AiredEpisodes is null
            || CompletedEpisodes is null
            || Availability is null
            || LifecycleState is null
            || LifecycleVersion is null
            || MissingScheduledConfirmations is null
            || AddedAt is null
            || UpdatedAt is null
            || MetadataFetchedAt is null
            || Seasons is null
            || SpecialEpisodeIdentities is null
            || Seasons.Any(season => season is null)
            || SpecialEpisodeIdentities.Any(identity => identity is null)
            || LegacyWatchlistItemId is not null
            || LegacyMigratedAt is not null
            || LegacyMigrationStatus is not null
            || LegacyMigrationReason is not null
            || Genres is null
            || Genres.Count != 0
            || OriginalLanguage is not null
            || TmdbVoteAverage is not null
            || TmdbVoteCount is not null)
        {
            throw new InvalidOperationException("tv_generation_row_invalid");
        }

        return new TvShow(
            PublicId,
            TraktId.Value,
            TvdbId,
            TmdbId,
            ImdbId,
            IdentityStatus.Value,
            Title,
            Year,
            Overview,
            PosterUrl,
            BackdropUrl,
            TraktStatus,
            InWatchlist.Value,
            AiredEpisodes.Value,
            CompletedEpisodes.Value,
            LastWatchedEpisode?.ToDomain(),
            NextEpisode?.ToDomain(),
            Seasons.Select(season => season.ToDomain()).ToArray(),
            SpecialEpisodeIdentities.Select(identity => identity.ToDomain()).ToArray(),
            Availability.ToDomain(),
            LifecycleState.Value,
            LastLifecycleEvent,
            LifecycleVersion.Value,
            MissingScheduledConfirmations.Value,
            AddedAt.Value,
            UpdatedAt.Value,
            MetadataFetchedAt.Value,
            GenerationId,
            LegacySourceId);
    }
}
