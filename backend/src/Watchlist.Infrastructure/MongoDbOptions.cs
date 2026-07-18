namespace Watchlist.Infrastructure;

public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public string WatchlistItemsCollectionName { get; init; } = string.Empty;

    public string SyncRunsCollectionName { get; init; } = string.Empty;

    public string PlexLibraryItemsCollectionName { get; init; } = "plex_library_items";

    public string LetterboxdSourceSnapshotsCollectionName { get; init; } =
        "letterboxd_source_snapshots";

    public string TvShowsCollectionName { get; init; } = "tv_shows";

    public string TvSyncManifestsCollectionName { get; init; } = "tv_sync_manifests";

    public string TvLifecycleEventsCollectionName { get; init; } = "tv_lifecycle_events";

    public string TraktConnectionsCollectionName { get; init; } = "trakt_connections";

    public string TmdbProviderCatalogCollectionName { get; init; } = "tmdb_provider_catalog";
}
