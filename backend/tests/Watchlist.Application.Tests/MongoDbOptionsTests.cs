using FluentAssertions;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoDbOptionsTests
{
    [Fact]
    public void Constructor_WhenPlexCollectionNameIsNotConfigured_UsesDefaultCollectionName()
    {
        MongoDbOptions options = new();

        options.PlexLibraryItemsCollectionName.Should().Be("plex_library_items");
        options.LetterboxdSourceSnapshotsCollectionName.Should().Be(
            "letterboxd_source_snapshots");
        options.TvShowsCollectionName.Should().Be("tv_shows");
        options.TvSyncManifestsCollectionName.Should().Be("tv_sync_manifests");
        options.TvLifecycleEventsCollectionName.Should().Be("tv_lifecycle_events");
        options.TraktConnectionsCollectionName.Should().Be("trakt_connections");
    }
}
