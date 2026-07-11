using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class WatchlistExportServiceTests
{
    private static readonly DateTimeOffset SnapshotTime =
        DateTimeOffset.Parse("2026-07-11T08:00:00Z");

    [Fact]
    public async Task GetMovieSyncSnapshotAsync_MapsEligibilityAndFreshness()
    {
        WatchlistExportService service = CreateService([
            new WatchlistExportMovieModel(
                "1297842",
                "tt27613895",
                "GOAT",
                2026,
                "/film/goat-2026/",
                [],
                TmdbId: 1297842,
                MetadataStatus: "enriched",
                AvailabilityStatus: AvailabilityStatus.NotOnPlex),
            new WatchlistExportMovieModel(
                "4951",
                "tt0147800",
                "10 Things I Hate About You",
                1999,
                "/film/10-things-i-hate-about-you/",
                ["Amazon Prime Video"],
                TmdbId: 4951,
                MetadataStatus: "enriched",
                AvailabilityStatus: AvailabilityStatus.AvailableOnPlex),
            new WatchlistExportMovieModel(
                "1418998",
                "tt35450621",
                "Karma",
                2026,
                "/film/karma-2026/",
                [],
                TmdbId: 1418998,
                MetadataStatus: "failed",
                AvailabilityStatus: AvailabilityStatus.UnknownMatch)
        ]);

        WorkerMovieSnapshotDto result =
            await service.GetMovieSyncSnapshotAsync(CancellationToken.None);

        result.GeneratedAt.Should().Be(SnapshotTime);
        result.LastSuccessfulMovieSyncAt.Should().Be(
            DateTimeOffset.Parse("2026-07-11T07:55:00Z"));
        result.Movies.Should().ContainEquivalentOf(new
        {
            TmdbId = (int?)1297842,
            MetadataStatus = "enriched",
            RadarrEligible = true,
            RadarrEligibilityReason = "no_owned_service"
        });
        result.Movies.Should().ContainEquivalentOf(new
        {
            TmdbId = (int?)4951,
            RadarrEligible = false,
            RadarrEligibilityReason = "owned_service_available"
        });
        result.Movies.Should().ContainEquivalentOf(new
        {
            TmdbId = (int?)1418998,
            RadarrEligible = false,
            RadarrEligibilityReason = "metadata_not_enriched"
        });
    }

    [Fact]
    public async Task GetMovieSyncSnapshotAsync_WhenIdentityInvalid_ExposesBlockingRow()
    {
        WatchlistExportService service = CreateService([
            new WatchlistExportMovieModel(
                "bad-source",
                null,
                "Malformed",
                2026,
                null,
                [],
                TmdbId: null,
                MetadataStatus: "failed",
                AvailabilityStatus: AvailabilityStatus.UnknownMatch)
        ]);

        WorkerMovieSnapshotDto result =
            await service.GetMovieSyncSnapshotAsync(CancellationToken.None);

        result.Movies.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            TmdbId = (int?)null,
            RadarrEligible = false,
            RadarrEligibilityReason = "invalid_tmdb_id"
        }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasNoOwnedVodAvailability_IncludesMovie()
    {
        WatchlistExportService service = CreateService(
        [
            new WatchlistExportMovieModel(
                "1297842",
                "tt27613895",
                "GOAT",
                2026,
                "/film/goat-2026/",
                [])
        ]);

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 1297842,
            ImdbId = "tt27613895",
            Title = "GOAT",
            ReleaseYear = "2026",
            CleanTitle = "/film/goat-2026/",
            Adult = false
        });
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasOwnedVodAvailability_ExcludesMovie()
    {
        WatchlistExportService service = CreateService(
        [
            new WatchlistExportMovieModel(
                "4951",
                "tt0147800",
                "10 Things I Hate About You",
                1999,
                "/film/10-things-i-hate-about-you/",
                ["Amazon Prime Video"])
        ]);

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenTmdbEnrichmentMissing_IncludesMovie()
    {
        WatchlistExportService service = CreateService(
        [
            new WatchlistExportMovieModel(
                "1418998",
                "tt35450621",
                "Karma",
                2026,
                "/film/karma-2026/",
                [])
        ]);

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Karma");
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenSourceIdCannotBeParsed_SkipsMovie()
    {
        WatchlistExportService service = CreateService(
        [
            new WatchlistExportMovieModel(
                "bad-source",
                "tt0000001",
                "Malformed",
                2026,
                "/film/malformed/",
                [])
        ]);

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenOptionalFieldsMissing_UsesEmptyStrings()
    {
        WatchlistExportService service = CreateService(
        [
            new WatchlistExportMovieModel(
                "1635594",
                null,
                "Ti Amo!",
                null,
                null,
                [])
        ]);

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        RadarrMovieExportItemDto item = result.Should().ContainSingle().Subject;
        item.ImdbId.Should().BeEmpty();
        item.ReleaseYear.Should().BeEmpty();
        item.CleanTitle.Should().BeEmpty();
    }

    [Fact]
    public Task GetSonarrTvAsync_ForV1_ReturnsEmptyList()
    {
        WatchlistExportService service = CreateService([]);

        return service.GetSonarrTvAsync(CancellationToken.None)
            .ContinueWith(task => task.Result.Should().BeEmpty(), CancellationToken.None);
    }

    private static WatchlistExportService CreateService(
        IReadOnlyList<WatchlistExportMovieModel> movies)
    {
        return new WatchlistExportService(
            new StubWatchlistExportRepository(movies),
            new StubSyncStatusReadRepository(),
            new StubTimeProvider());
    }

    private sealed class StubWatchlistExportRepository(
        IReadOnlyList<WatchlistExportMovieModel> movies) : IWatchlistExportRepository
    {
        public Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(movies);
        }
    }

    private sealed class StubSyncStatusReadRepository : ISyncStatusReadRepository
    {
        public Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<SyncStatusDto?>(null);
        }

        public Task<SyncStatusDto?> GetLatestByStatusAsync(
            string status,
            CancellationToken cancellationToken)
        {
            status.Should().Be(SyncRunStatuses.PlexMoviesCompleted);
            return Task.FromResult<SyncStatusDto?>(new SyncStatusDto(
                status,
                DateTimeOffset.Parse("2026-07-11T07:55:00Z")));
        }
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => SnapshotTime;
    }
}
