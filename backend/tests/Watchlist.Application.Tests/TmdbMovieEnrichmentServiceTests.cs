using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbMovieEnrichmentServiceTests
{
    private static readonly DateTimeOffset SyncTime = DateTimeOffset.Parse("2026-06-04T12:00:00Z");

    [Fact]
    public async Task SyncMoviesAsync_WhenRepositoryReturnsMixedRecords_EnrichesOnlyLetterboxdMovies()
    {
        FakeTmdbMovieClient client = new();
        client.MetadataByCandidateId[1297842] = CreateMetadata(1297842, "GOAT");
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842"),
            CreateWriteModel("movie-tmdb-1297842", MediaType.Movie, WatchlistSource.Tmdb, "1297842"),
            CreateWriteModel("tv-tmdb-1", MediaType.TvShow, WatchlistSource.Tmdb, "1")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbMovieEnrichmentResultDto result = await service.SyncMoviesAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.StartedAt.Should().Be(SyncTime);
        result.FinishedAt.Should().Be(SyncTime);
        result.ItemsMatched.Should().Be(1);
        result.ItemsEnriched.Should().Be(1);
        result.ItemsNotFound.Should().Be(0);
        result.ItemsFailed.Should().Be(0);
        client.Requests.Should().ContainSingle().Which.Should().Be((1297842, "tt1297842"));
        repository.Updates.Should().ContainSingle(update => update.Id == "movie-letterboxd-1297842")
            .Which.Update.Should().Match<TmdbMovieMetadataUpdate>(update =>
                update.TmdbId == 1297842
                && update.TmdbTitle == "GOAT"
                && update.RuntimeMinutes == 96
                && update.OriginalLanguage == "en"
                && update.TmdbVoteAverage == 7.4
                && update.TmdbVoteCount == 812
                && update.MetadataStatus == "enriched"
                && update.MetadataError == null);
    }

    [Fact]
    public async Task SyncMovieAsync_WhenIdMissingOrNotLetterboxdMovie_ReturnsNull()
    {
        FakeTmdbMovieClient client = new();
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-tmdb-1297842", MediaType.Movie, WatchlistSource.Tmdb, "1297842"),
            CreateWriteModel("tv-tmdb-1", MediaType.TvShow, WatchlistSource.Tmdb, "1")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbSingleMovieEnrichmentResultDto? missing = await service.SyncMovieAsync(
            "missing",
            CancellationToken.None);
        TmdbSingleMovieEnrichmentResultDto? nonLetterboxd = await service.SyncMovieAsync(
            "movie-tmdb-1297842",
            CancellationToken.None);

        missing.Should().BeNull();
        nonLetterboxd.Should().BeNull();
        client.Requests.Should().BeEmpty();
        repository.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncMoviesAsync_DerivesOwnedServicesFromPlFlatrateAndVodFromPlAndUsProviderLists()
    {
        TmdbMovieProviderDataDto providers = new(new Dictionary<string, TmdbRegionWatchProvidersDto>
        {
            ["PL"] = new(
                [
                    new TmdbWatchProviderDto(119, "MAX", "/max.jpg", 1),
                    new TmdbWatchProviderDto(1899, "Prime Video", "/prime.jpg", 2),
                    new TmdbWatchProviderDto(8, "Netflix", "/netflix.jpg", 3)
                ],
                [new TmdbWatchProviderDto(4, "Apple TV", "/apple.jpg", 4)],
                []),
            ["US"] = new(
                [],
                [],
                [new TmdbWatchProviderDto(5, "Amazon Video", "/amazon.jpg", 5)]),
            ["DE"] = new(
                [new TmdbWatchProviderDto(6, "SkyShowtime", "/sky.jpg", 6)],
                [],
                [])
        });
        FakeTmdbMovieClient client = new();
        client.MetadataByCandidateId[1297842] = CreateMetadata(1297842, "GOAT", providers);
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        await service.SyncMoviesAsync(CancellationToken.None);

        TmdbMovieMetadataUpdate update = repository.Updates.Single().Update;
        update.OwnedServiceAvailability.Should().Equal("MAX", "Prime Video");
        update.ReleasedOnVod.Should().BeTrue();
        update.VodRegions.Should().Equal("PL", "US");
    }

    [Fact]
    public async Task SyncMoviesAsync_MatchesOwnedSubscriptionsByConfiguredIdNotDisplayName()
    {
        TmdbMovieProviderDataDto providers = new(new Dictionary<string, TmdbRegionWatchProvidersDto>
        {
            ["PL"] = new(
                [
                    new TmdbWatchProviderDto(
                        119,
                        "Renamed upstream without notice",
                        "/owned.jpg",
                        1),
                    new TmdbWatchProviderDto(8, "Max", "/misleading.jpg", 2)
                ],
                [],
                [])
        });
        FakeTmdbMovieClient client = new();
        client.MetadataByCandidateId[1297842] = CreateMetadata(1297842, "GOAT", providers);
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        await service.SyncMoviesAsync(CancellationToken.None);

        TmdbMovieMetadataUpdate update = repository.Updates.Single().Update;
        update.OwnedServiceAvailability.Should().Equal("Renamed upstream without notice");
        update.Providers.Regions["PL"].Flatrate.Should().Equal(
            providers.Regions["PL"].Flatrate);
    }

    [Fact]
    public async Task SyncMoviesAsync_WhenOnlyRentOrBuyOwnedProviderExists_DoesNotSetOwnedAvailability()
    {
        TmdbMovieProviderDataDto providers = new(new Dictionary<string, TmdbRegionWatchProvidersDto>
        {
            ["PL"] = new(
                [],
                [new TmdbWatchProviderDto(1, "HBO Max", "/max.jpg", 1)],
                [new TmdbWatchProviderDto(2, "Amazon Prime Video", "/prime.jpg", 2)])
        });
        FakeTmdbMovieClient client = new();
        client.MetadataByCandidateId[1297842] = CreateMetadata(1297842, "GOAT", providers);
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        await service.SyncMoviesAsync(CancellationToken.None);

        TmdbMovieMetadataUpdate update = repository.Updates.Single().Update;
        update.OwnedServiceAvailability.Should().BeEmpty();
        update.ReleasedOnVod.Should().BeTrue();
        update.VodRegions.Should().Equal("PL");
    }

    [Fact]
    public async Task SyncMoviesAsync_WhenMovieNotFound_StoresNotFoundAndCountsItem()
    {
        FakeTmdbMovieClient client = new();
        client.ExceptionsByCandidateId[1297842] = new TmdbMovieNotFoundException("TMDB movie 1297842 was not found.");
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbMovieEnrichmentResultDto result = await service.SyncMoviesAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.ItemsMatched.Should().Be(1);
        result.ItemsEnriched.Should().Be(0);
        result.ItemsNotFound.Should().Be(1);
        result.ItemsFailed.Should().Be(0);
        repository.Updates.Should().ContainSingle().Which.Update.Should().Match<TmdbMovieMetadataUpdate>(update =>
            update.MetadataStatus == "not_found"
            && update.MetadataError == "TMDB movie 1297842 was not found."
            && update.UpdatedAt == SyncTime);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncMoviesAsync_WhenMovieFails_StoresFailedAndReturnsPartial(bool unavailable)
    {
        Exception exception = unavailable
            ? new TmdbUnavailableException("TMDB returned HTTP 503.")
            : new InvalidOperationException("Unexpected.");
        FakeTmdbMovieClient client = new();
        client.ExceptionsByCandidateId[1297842] = exception;
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbMovieEnrichmentResultDto result = await service.SyncMoviesAsync(CancellationToken.None);

        result.Status.Should().Be("partial");
        result.ItemsMatched.Should().Be(1);
        result.ItemsEnriched.Should().Be(0);
        result.ItemsNotFound.Should().Be(0);
        result.ItemsFailed.Should().Be(1);
        repository.Updates.Should().ContainSingle().Which.Update.Should().Match<TmdbMovieMetadataUpdate>(update =>
            update.MetadataStatus == "failed"
            && update.MetadataError == exception.Message
            && update.UpdatedAt == SyncTime);
    }

    [Fact]
    public async Task SyncMovieAsync_WhenTmdbUnavailable_BubblesException()
    {
        FakeTmdbMovieClient client = new();
        client.ExceptionsByCandidateId[1297842] = new TmdbUnavailableException("TMDB returned HTTP 503.");
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        Func<Task> action = () => service.SyncMovieAsync("movie-letterboxd-1297842", CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
        repository.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncMovieAsync_WhenMovieNotFound_StoresNotFoundAndReturnsNotFound()
    {
        FakeTmdbMovieClient client = new();
        client.ExceptionsByCandidateId[1297842] = new TmdbMovieNotFoundException("TMDB movie 1297842 was not found.");
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-1297842", MediaType.Movie, WatchlistSource.Letterboxd, "1297842")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbSingleMovieEnrichmentResultDto? result = await service.SyncMovieAsync(
            "movie-letterboxd-1297842",
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be("not_found");
        result.Id.Should().Be("movie-letterboxd-1297842");
        result.TmdbId.Should().BeNull();
        repository.Updates.Should().ContainSingle().Which.Update.Should().Match<TmdbMovieMetadataUpdate>(update =>
            update.MetadataStatus == "not_found"
            && update.MetadataError == "TMDB movie 1297842 was not found."
            && update.UpdatedAt == SyncTime);
    }

    [Fact]
    public async Task SyncMoviesAsync_WhenSourceIdCannotBeParsed_StoresFailedWithoutCallingTmdb()
    {
        FakeTmdbMovieClient client = new();
        FakeTmdbMovieMetadataRepository repository = new([
            CreateWriteModel("movie-letterboxd-bad", MediaType.Movie, WatchlistSource.Letterboxd, "bad")
        ]);
        TmdbMovieEnrichmentService service = CreateService(client, repository);

        TmdbMovieEnrichmentResultDto result = await service.SyncMoviesAsync(CancellationToken.None);

        result.Status.Should().Be("partial");
        result.ItemsMatched.Should().Be(1);
        result.ItemsFailed.Should().Be(1);
        client.Requests.Should().BeEmpty();
        repository.Updates.Should().ContainSingle().Which.Update.Should().Match<TmdbMovieMetadataUpdate>(update =>
            update.MetadataStatus == "failed"
            && update.MetadataError == "Letterboxd movie source id 'bad' is not a valid TMDB id.");
    }

    private static TmdbMovieEnrichmentService CreateService(
        ITmdbMovieClient client,
        ITmdbMovieMetadataRepository repository)
    {
        return new TmdbMovieEnrichmentService(
            client,
            repository,
            new FakeTimeProvider(SyncTime),
            new TmdbEnrichmentSettings(
                "PL",
                [119, 1899, 1773],
                TimeSpan.FromDays(1),
                TimeSpan.FromHours(24)));
    }

    private static WatchlistItemWriteModel CreateWriteModel(
        string id,
        MediaType mediaType,
        WatchlistSource source,
        string sourceId)
    {
        WatchlistItem item = new(
            id,
            mediaType,
            source,
            sourceId,
            "Movie",
            2026,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"));

        return new WatchlistItemWriteModel(item, $"tt{sourceId}", $"/film/{sourceId}/");
    }

    private static TmdbMovieMetadataDto CreateMetadata(
        int tmdbId,
        string title,
        TmdbMovieProviderDataDto? providers = null)
    {
        return new TmdbMovieMetadataDto(
            new TmdbMovieDetailsDto(
                tmdbId,
                $"tt{tmdbId}",
                title,
                $"{title} Original",
                "Overview",
                "2026-02-13",
                "/poster.jpg",
                "/backdrop.jpg",
                "https://image.tmdb.org/t/p/w500/poster.jpg",
                "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
                ["Drama"],
                96,
                "en",
                7.4,
                812),
            providers ?? new TmdbMovieProviderDataDto(new Dictionary<string, TmdbRegionWatchProvidersDto>()));
    }

    private sealed class FakeTmdbMovieClient : ITmdbMovieClient
    {
        public Dictionary<int, TmdbMovieMetadataDto> MetadataByCandidateId { get; } = [];

        public Dictionary<int, Exception> ExceptionsByCandidateId { get; } = [];

        public List<(int CandidateTmdbId, string? ImdbId)> Requests { get; } = [];

        public Task<TmdbMovieMetadataDto> GetMovieMetadataAsync(
            int candidateTmdbId,
            string? imdbId,
            CancellationToken cancellationToken)
        {
            Requests.Add((candidateTmdbId, imdbId));
            if (ExceptionsByCandidateId.TryGetValue(candidateTmdbId, out Exception? exception))
            {
                throw exception;
            }

            return Task.FromResult(MetadataByCandidateId[candidateTmdbId]);
        }
    }

    private sealed class FakeTmdbMovieMetadataRepository(
        IReadOnlyList<WatchlistItemWriteModel> items) : ITmdbMovieMetadataRepository
    {
        private readonly IReadOnlyList<WatchlistItemWriteModel> items = items;

        public List<(string Id, TmdbMovieMetadataUpdate Update)> Updates { get; } = [];

        public Task<IReadOnlyList<WatchlistItemWriteModel>> GetLetterboxdMoviesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }

        public Task<WatchlistItemWriteModel?> GetLetterboxdMovieAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(items.FirstOrDefault(item => item.Item.Id == id));
        }

        public Task ApplyTmdbMetadataAsync(
            string id,
            TmdbMovieMetadataUpdate update,
            CancellationToken cancellationToken)
        {
            Updates.Add((id, update));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
