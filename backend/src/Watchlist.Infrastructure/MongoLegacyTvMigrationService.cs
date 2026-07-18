using System.Globalization;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoLegacyTvMigrationService(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options,
    TimeProvider timeProvider) : ILegacyTvMigrationService
{
    private const string MigratedStatus = "migrated";
    private const string QuarantinedStatus = "quarantined";
    private const string ExactTvdbIdentity = "exact_tvdb_identity";
    private const string ExactTmdbIdentity = "exact_tmdb_identity";
    private const string SourceTmdbConflict = "source_tmdb_conflict";
    private const string SourceIdInvalid = "source_id_invalid";
    private const string DuplicateTvdbIdentity = "duplicate_tvdb_identity";
    private const string CollisionCode = "legacy_tv_migration_collision";

    public async Task<LegacyTvMigrationResult> MigrateAsync(
        CancellationToken cancellationToken)
    {
        IMongoCollection<MongoWatchlistItemDocument> legacyItems =
            database.GetCollection<MongoWatchlistItemDocument>(
                options.Value.WatchlistItemsCollectionName);
        IMongoCollection<MongoTvShowDocument> tvShows =
            database.GetCollection<MongoTvShowDocument>(options.Value.TvShowsCollectionName);
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filters =
            Builders<MongoWatchlistItemDocument>.Filter;
        List<MongoWatchlistItemDocument> sourceRows = await legacyItems
            .Find(filters.Eq(document => document.MediaType, MediaType.TvShow))
            .SortBy(document => document.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<int> duplicateTvdbIds = sourceRows
            .Where(document => document.TvdbId is > 0)
            .GroupBy(document => document.TvdbId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        DateTimeOffset migratedAt = timeProvider.GetUtcNow();
        int migratedCount = 0;
        int quarantinedCount = 0;

        foreach (MongoWatchlistItemDocument source in sourceRows)
        {
            MongoTvShowDocument document = CreateLegacyDocument(
                source,
                duplicateTvdbIds,
                migratedAt);
            bool inserted = await InsertOrValidateAsync(
                    tvShows,
                    document,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!inserted)
            {
                continue;
            }

            if (string.Equals(
                document.LegacyMigrationStatus,
                QuarantinedStatus,
                StringComparison.Ordinal))
            {
                quarantinedCount++;
            }
            else
            {
                migratedCount++;
            }
        }

        return new LegacyTvMigrationResult(migratedCount, quarantinedCount);
    }

    private async Task<bool> InsertOrValidateAsync(
        IMongoCollection<MongoTvShowDocument> tvShows,
        MongoTvShowDocument expected,
        CancellationToken cancellationToken)
    {
        UpdateDefinition<MongoTvShowDocument> insert = CreateInsertDefinition(expected);
        try
        {
            UpdateResult result = await tvShows.UpdateOneAsync(
                    document => document.Id == expected.Id,
                    insert,
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.UpsertedId is not null)
            {
                return true;
            }
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A concurrent startup may have inserted the deterministic row first.
        }

        MongoTvShowDocument? existing;
        try
        {
            existing = await tvShows
                .Find(document => document.Id == expected.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw new InvalidOperationException(CollisionCode);
        }

        if (existing is null || !HasMatchingSemanticPayload(existing, expected))
        {
            throw new InvalidOperationException(CollisionCode);
        }

        return false;
    }

    private static MongoTvShowDocument CreateLegacyDocument(
        MongoWatchlistItemDocument source,
        IReadOnlySet<int> duplicateTvdbIds,
        DateTimeOffset migratedAt)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            throw new InvalidOperationException(CollisionCode);
        }

        int sourceTmdbId = 0;
        bool sourceIdIsValid = source.Source == WatchlistSource.Tmdb
            && int.TryParse(
                source.SourceId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sourceTmdbId)
            && sourceTmdbId > 0;
        bool sourceTmdbConflicts = sourceIdIsValid
            && source.TmdbId is not null
            && source.TmdbId != sourceTmdbId;
        bool tvdbIsDuplicated = source.TvdbId is > 0
            && duplicateTvdbIds.Contains(source.TvdbId.Value);
        string status;
        string reason;

        if (!sourceIdIsValid)
        {
            status = QuarantinedStatus;
            reason = SourceIdInvalid;
        }
        else if (sourceTmdbConflicts)
        {
            status = QuarantinedStatus;
            reason = SourceTmdbConflict;
        }
        else if (tvdbIsDuplicated)
        {
            status = QuarantinedStatus;
            reason = DuplicateTvdbIdentity;
        }
        else
        {
            status = MigratedStatus;
            reason = source.TvdbId is > 0 ? ExactTvdbIdentity : ExactTmdbIdentity;
        }

        int? tmdbId = source.TmdbId ?? (sourceIdIsValid ? sourceTmdbId : null);
        return new MongoTvShowDocument
        {
            Id = $"legacy:{source.Id}",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind,
            TvdbId = source.TvdbId,
            TmdbId = tmdbId,
            ImdbId = source.ImdbId,
            IdentityStatus = TvIdentityStatus.LegacyUnresolved,
            Title = source.Title,
            Year = source.Year,
            Overview = source.Overview,
            PosterUrl = source.PosterUrl,
            BackdropUrl = source.BackdropUrl,
            AddedAt = source.AddedAt,
            UpdatedAt = source.UpdatedAt,
            LegacySourceId = source.SourceId,
            LegacyWatchlistItemId = source.Id,
            LegacyMigratedAt = migratedAt,
            LegacyMigrationStatus = status,
            LegacyMigrationReason = reason,
            Genres = source.Genres is null ? [] : source.Genres.ToArray(),
            OriginalLanguage = source.OriginalLanguage,
            TmdbVoteAverage = source.TmdbVoteAverage,
            TmdbVoteCount = source.TmdbVoteCount
        };
    }

    private static UpdateDefinition<MongoTvShowDocument> CreateInsertDefinition(
        MongoTvShowDocument document)
    {
        UpdateDefinitionBuilder<MongoTvShowDocument> update =
            Builders<MongoTvShowDocument>.Update;
        List<UpdateDefinition<MongoTvShowDocument>> fields =
        [
            update.SetOnInsert(stored => stored.DocumentKind, document.DocumentKind),
            update.SetOnInsert(stored => stored.TvdbId, document.TvdbId),
            update.SetOnInsert(stored => stored.TmdbId, document.TmdbId),
            update.SetOnInsert(stored => stored.ImdbId, document.ImdbId),
            update.SetOnInsert(stored => stored.IdentityStatus, document.IdentityStatus),
            update.SetOnInsert(stored => stored.Title, document.Title),
            update.SetOnInsert(stored => stored.Year, document.Year),
            update.SetOnInsert(stored => stored.Overview, document.Overview),
            update.SetOnInsert(stored => stored.PosterUrl, document.PosterUrl),
            update.SetOnInsert(stored => stored.BackdropUrl, document.BackdropUrl),
            update.SetOnInsert(stored => stored.AddedAt, document.AddedAt),
            update.SetOnInsert(stored => stored.UpdatedAt, document.UpdatedAt),
            update.SetOnInsert(stored => stored.LegacySourceId, document.LegacySourceId),
            update.SetOnInsert(
                stored => stored.LegacyWatchlistItemId,
                document.LegacyWatchlistItemId),
            update.SetOnInsert(stored => stored.LegacyMigratedAt, document.LegacyMigratedAt),
            update.SetOnInsert(
                stored => stored.LegacyMigrationStatus,
                document.LegacyMigrationStatus),
            update.SetOnInsert(
                stored => stored.LegacyMigrationReason,
                document.LegacyMigrationReason),
            update.SetOnInsert(stored => stored.Genres, document.Genres),
            update.SetOnInsert(stored => stored.OriginalLanguage, document.OriginalLanguage),
            update.SetOnInsert(stored => stored.TmdbVoteAverage, document.TmdbVoteAverage),
            update.SetOnInsert(stored => stored.TmdbVoteCount, document.TmdbVoteCount)
        ];
        return update.Combine(fields);
    }

    private static bool HasMatchingSemanticPayload(
        MongoTvShowDocument existing,
        MongoTvShowDocument expected)
    {
        return string.Equals(
                existing.DocumentKind,
                MongoTvShowDocument.LegacyDocumentKind,
                StringComparison.Ordinal)
            && existing.GenerationId is null
            && existing.TraktId is null
            && existing.PublicId is null
            && existing.TvdbId == expected.TvdbId
            && existing.TmdbId == expected.TmdbId
            && string.Equals(existing.ImdbId, expected.ImdbId, StringComparison.Ordinal)
            && existing.IdentityStatus == TvIdentityStatus.LegacyUnresolved
            && string.Equals(existing.Title, expected.Title, StringComparison.Ordinal)
            && existing.Year == expected.Year
            && string.Equals(existing.Overview, expected.Overview, StringComparison.Ordinal)
            && string.Equals(existing.PosterUrl, expected.PosterUrl, StringComparison.Ordinal)
            && string.Equals(existing.BackdropUrl, expected.BackdropUrl, StringComparison.Ordinal)
            && existing.TraktStatus is null
            && existing.InWatchlist is null
            && existing.AiredEpisodes is null
            && existing.CompletedEpisodes is null
            && existing.LastWatchedEpisode is null
            && existing.NextEpisode is null
            && (existing.Seasons?.Count ?? 0) == 0
            && (existing.SpecialEpisodeIdentities?.Count ?? 0) == 0
            && existing.Availability is null
            && existing.LifecycleState is null
            && existing.LastLifecycleEvent is null
            && existing.LifecycleVersion is null
            && existing.MissingScheduledConfirmations is null
            && existing.AddedAt == expected.AddedAt
            && existing.UpdatedAt == expected.UpdatedAt
            && existing.MetadataFetchedAt is null
            && string.Equals(
                existing.LegacySourceId,
                expected.LegacySourceId,
                StringComparison.Ordinal)
            && string.Equals(
                existing.LegacyWatchlistItemId,
                expected.LegacyWatchlistItemId,
                StringComparison.Ordinal)
            && existing.LegacyMigratedAt is not null
            && string.Equals(
                existing.LegacyMigrationStatus,
                expected.LegacyMigrationStatus,
                StringComparison.Ordinal)
            && string.Equals(
                existing.LegacyMigrationReason,
                expected.LegacyMigrationReason,
                StringComparison.Ordinal)
            && (existing.Genres ?? []).SequenceEqual(
                expected.Genres,
                StringComparer.Ordinal)
            && string.Equals(
                existing.OriginalLanguage,
                expected.OriginalLanguage,
                StringComparison.Ordinal)
            && existing.TmdbVoteAverage == expected.TmdbVoteAverage
            && existing.TmdbVoteCount == expected.TmdbVoteCount;
    }
}
