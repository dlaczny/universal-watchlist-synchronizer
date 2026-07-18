using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Watchlist.Infrastructure;

public sealed class MongoTvIndexHostedService(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        MongoDbOptions configured = options.Value;
        IMongoCollection<MongoTvShowDocument> shows =
            database.GetCollection<MongoTvShowDocument>(configured.TvShowsCollectionName);
        IMongoCollection<MongoTvSyncManifestDocument> manifests =
            database.GetCollection<MongoTvSyncManifestDocument>(
                configured.TvSyncManifestsCollectionName);
        IMongoCollection<MongoTvLifecycleEventDocument> lifecycleEvents =
            database.GetCollection<MongoTvLifecycleEventDocument>(
                configured.TvLifecycleEventsCollectionName);

        IndexKeysDefinitionBuilder<MongoTvShowDocument> showKeys =
            Builders<MongoTvShowDocument>.IndexKeys;
        FilterDefinitionBuilder<MongoTvShowDocument> showFilters =
            Builders<MongoTvShowDocument>.Filter;
        await shows.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<MongoTvShowDocument>(
                    showKeys
                        .Ascending(document => document.DocumentKind)
                        .Ascending(document => document.GenerationId)
                        .Ascending(document => document.TraktId),
                    new CreateIndexOptions<MongoTvShowDocument>
                    {
                        Name = "ux_tv_shows_generation_identity",
                        Unique = true,
                        PartialFilterExpression = showFilters.Eq(
                            document => document.DocumentKind,
                            MongoTvShowDocument.GenerationDocumentKind)
                    }),
                new CreateIndexModel<MongoTvShowDocument>(
                    showKeys
                        .Ascending(document => document.DocumentKind)
                        .Ascending(document => document.TvdbId),
                    new CreateIndexOptions
                    {
                        Name = "ix_tv_shows_document_kind_tvdb_id"
                    }),
                new CreateIndexModel<MongoTvShowDocument>(
                    showKeys
                        .Ascending(document => document.DocumentKind)
                        .Ascending(document => document.TmdbId),
                    new CreateIndexOptions
                    {
                        Name = "ix_tv_shows_document_kind_tmdb_id"
                    })
            ],
            cancellationToken);

        IndexKeysDefinitionBuilder<MongoTvSyncManifestDocument> manifestKeys =
            Builders<MongoTvSyncManifestDocument>.IndexKeys;
        FilterDefinitionBuilder<MongoTvSyncManifestDocument> manifestFilters =
            Builders<MongoTvSyncManifestDocument>.Filter;
        await manifests.Indexes.CreateOneAsync(
            new CreateIndexModel<MongoTvSyncManifestDocument>(
                manifestKeys.Ascending(document => document.GenerationId),
                new CreateIndexOptions<MongoTvSyncManifestDocument>
                {
                    Name = "ux_tv_sync_manifests_generation_id",
                    Unique = true,
                    PartialFilterExpression = manifestFilters.Eq(
                        document => document.DocumentKind,
                        MongoTvSyncManifestDocument.ManifestDocumentKind)
                }),
            cancellationToken: cancellationToken);

        IndexKeysDefinitionBuilder<MongoTvLifecycleEventDocument> eventKeys =
            Builders<MongoTvLifecycleEventDocument>.IndexKeys;
        await lifecycleEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<MongoTvLifecycleEventDocument>(
                eventKeys
                    .Ascending(document => document.GenerationId)
                    .Ascending(document => document.TraktId)
                    .Ascending(document => document.LifecycleVersion),
                new CreateIndexOptions
                {
                    Name = "ix_tv_lifecycle_events_generation_trakt_version"
                }),
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
