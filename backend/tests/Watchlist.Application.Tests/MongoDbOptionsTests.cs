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
    }
}
