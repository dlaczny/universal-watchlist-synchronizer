using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class SeedDataTests
{
    [Fact]
    public void WatchlistItems_ContainsOnlyExistingMovieSamples()
    {
        IReadOnlyList<WatchlistItem> items = SeedData.WatchlistItems;

        items.Should().HaveCount(2);
        items.Should().OnlyContain(item => item.MediaType == MediaType.Movie);
        items.Should().ContainSingle(item =>
            item.Id == "movie-dune-part-two"
            && item.MediaType == MediaType.Movie
            && item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            && item.AddedAt == DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"));
        items.Should().ContainSingle(item =>
            item.Id == "movie-unreleased-example"
            && item.MediaType == MediaType.Movie
            && item.AvailabilityStatus == AvailabilityStatus.Unreleased
            && item.AddedAt == DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"));
    }

    [Fact]
    public void SyncRuns_ContainsSeededBootstrapRecord()
    {
        IReadOnlyList<MongoSyncRunDocument> syncRuns = SeedData.SyncRuns;

        syncRuns.Should().ContainSingle(syncRun =>
            syncRun.Id == "seeded-bootstrap"
            && syncRun.Status == "seeded"
            && syncRun.LastSuccessfulSyncAt == DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"));
    }
}
