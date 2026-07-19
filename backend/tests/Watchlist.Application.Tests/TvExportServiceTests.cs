using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvExportServiceTests
{
    [Fact]
    public async Task Export_MapsTheSinglePublishedGenerationAndKeepsMutationsLocked()
    {
        TvExportService service = new(new FakeRepository(CreatePublishedGeneration()));

        WorkerTvSnapshotDto? snapshot = await service.GetTvSyncSnapshotAsync(CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot!.SchemaVersion.Should().Be("1");
        snapshot.GenerationId.Should().Be("tv-generation");
        snapshot.MutationCapable.Should().BeFalse();
        snapshot.HealthReasons.Should().ContainInOrder(
            "plex_history_phase_not_implemented", "worker_tv_mutation_disabled");
        snapshot.PlexHistory.Should().Be(new WorkerTvPlexHistoryDto(false, false, null, null, null, null, null, null));
        WorkerTvShowDto show = snapshot.Shows.Should().ContainSingle().Which;
        show.LifecycleState.Should().Be("source_removed");
        show.SonarrDesired.Should().BeFalse();
        show.PlexWatchlistDesired.Should().BeTrue();
        show.Blockers.Should().Equal("phase_1_read_only");
        show.Seasons.Single(season => season.SeasonNumber == 0).Episodes.Should().ContainSingle()
            .Which.TraktEpisodeId.Should().Be(800);
        show.Seasons.Single(season => season.SeasonNumber == 1).SearchAiredUnwatchedEpisodes.Should().Equal(1);
    }

    private static PublishedTvGeneration CreatePublishedGeneration()
    {
        DateTimeOffset timestamp = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        TvProviderAvailability availability = new(TvProviderState.Unknown, "PL", null, null, []);
        TvEpisodeProgress episode = new(700, 701, 1, 1, "Pilot", timestamp.AddDays(-1), false, null);
        TvShow show = new(
            "tv-trakt-70", 70, 71, 72, "tt0000070", TvIdentityStatus.Verified,
            "Exported Show", 2026, null, null, null, "ended", false, 1, 0,
            null, episode, [new TvSeasonProgress(1, 1, 0, false, availability, [episode])],
            [new TvSpecialEpisodeIdentity(800, 801, 0, 1)], availability,
            TvLifecycleState.SourceRemoved, "tv:70:3:source_removed", 3, 2,
            timestamp.AddDays(-2), timestamp, timestamp, "tv-generation", null);
        TvGenerationManifest manifest = new(
            "tv-generation", null, TvGenerationKind.ScheduledFull, timestamp.AddMinutes(-1), timestamp,
            timestamp, new TraktActivityCursor(timestamp.AddMinutes(-2), timestamp.AddMinutes(-2)),
            1, 1, 1, 1, "v1", new Dictionary<string, string>(), "membership", "progress",
            null, null, timestamp, "valid", [], [], [], false,
            ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"], []);
        return new PublishedTvGeneration(manifest, [show]);
    }

    private sealed class FakeRepository(PublishedTvGeneration published) : ITvGenerationRepository
    {
        public Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PublishedTvGeneration?>(published);
    }
}
