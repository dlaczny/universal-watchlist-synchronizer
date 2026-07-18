using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTmdbProviderCatalogRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITmdbProviderCatalogRepository
{
    private readonly IMongoCollection<MongoTmdbProviderCatalogDocument> catalog =
        database.GetCollection<MongoTmdbProviderCatalogDocument>(
            options.Value.TmdbProviderCatalogCollectionName);

    public async Task<TmdbProviderCatalogSnapshot?> GetAsync(
        CancellationToken cancellationToken)
    {
        MongoTmdbProviderCatalogDocument? document = await GetDocumentAsync(cancellationToken);
        return document?.ToSnapshot();
    }

    public async Task<DateTimeOffset?> GetLastAttemptAtAsync(
        CancellationToken cancellationToken)
    {
        MongoTmdbProviderCatalogDocument? document = await GetDocumentAsync(cancellationToken);
        return document?.GetLastAttemptAt();
    }

    public async Task ReplaceAsync(
        TmdbProviderCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TmdbProviderCatalogSnapshot validatedSnapshot = new(
            snapshot.CatalogFetchedAt,
            snapshot.RegionsFetchedAt,
            snapshot.Stale,
            snapshot.LastErrorCode,
            snapshot.LastErrorAt,
            RevalidateProviders(snapshot.Providers),
            snapshot.RegionCodes.ToArray());
        MongoTmdbProviderCatalogDocument document =
            MongoTmdbProviderCatalogDocument.FromSnapshot(validatedSnapshot);
        await catalog.ReplaceOneAsync(
            stored => stored.Id == MongoTmdbProviderCatalogDocument.SingletonId,
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    private static IReadOnlyList<TmdbWatchProviderCatalogEntryDto> RevalidateProviders(
        IReadOnlyList<TmdbWatchProviderCatalogEntryDto> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return providers
            .Select(provider => provider is null
                ? throw new ArgumentException("Catalog providers cannot contain null entries.", nameof(providers))
                : new TmdbWatchProviderCatalogEntryDto(
                    provider.ProviderId,
                    provider.ProviderName,
                    provider.LogoPath,
                    provider.DisplayPriority))
            .ToArray();
    }

    public async Task MarkStaleAsync(
        string errorCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        string validatedCode = ValidateErrorCode(errorCode);
        DateTimeOffset validatedTime = ValidateUtc(failedAt);
        UpdateDefinitionBuilder<MongoTmdbProviderCatalogDocument> update =
            Builders<MongoTmdbProviderCatalogDocument>.Update;
        await catalog.UpdateOneAsync(
            stored => stored.Id == MongoTmdbProviderCatalogDocument.SingletonId,
            update
                .SetOnInsert(stored => stored.Id, MongoTmdbProviderCatalogDocument.SingletonId)
                .Set(stored => stored.Stale, true)
                .Set(stored => stored.LastErrorCode, validatedCode)
                .Set(stored => stored.LastErrorAt, validatedTime),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    private async Task<MongoTmdbProviderCatalogDocument?> GetDocumentAsync(
        CancellationToken cancellationToken)
    {
        return await catalog
            .Find(document => document.Id == MongoTmdbProviderCatalogDocument.SingletonId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string ValidateErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode)
            || errorCode.Any(character => character is not (>= 'a' and <= 'z')
                && character is not (>= '0' and <= '9')
                && character != '_'))
        {
            throw new ArgumentException("Error code must be stable and redacted.", nameof(errorCode));
        }

        return errorCode;
    }

    private static DateTimeOffset ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero
            || value == DateTimeOffset.MinValue
            || value == DateTimeOffset.MaxValue)
        {
            throw new ArgumentException("Failure time must be a finite UTC timestamp.", nameof(value));
        }

        return value;
    }
}
