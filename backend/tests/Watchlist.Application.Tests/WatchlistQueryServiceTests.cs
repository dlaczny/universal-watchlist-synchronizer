using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class WatchlistQueryServiceTests
{
    private static readonly DateTimeOffset AddedAt = new(2026, 5, 20, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 5, 25, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetItemsAsync_WhenCollectionAll_ReturnsMoviesAndTv()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("old-movie", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"))
        ];
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository(items),
            new StubTvShowReadRepository([CreateTvShow("tv-trakt-12345", TvLifecycleState.Active)]));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.NotOnPlex, AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending,
            null);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Id).Should().Equal("tv-trakt-12345", "old-movie");
    }

    [Theory]
    [InlineData(null, "tv-trakt-12345")]
    [InlineData(TvBrowseState.Active, "tv-trakt-12345")]
    [InlineData(TvBrowseState.CaughtUp, "tv-trakt-45678")]
    [InlineData(TvBrowseState.Retired, "tv-trakt-78901")]
    public async Task GetItemsAsync_WhenCollectionTv_AppliesPublishedTvState(
        TvBrowseState? state,
        string expectedId)
    {
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository([]),
            new StubTvShowReadRepository(
            [
                CreateTvShow("tv-trakt-12345", TvLifecycleState.Active),
                CreateTvShow("tv-trakt-45678", TvLifecycleState.CaughtUp),
                CreateTvShow("tv-trakt-78901", TvLifecycleState.RetiredTerminal),
                CreateTvShow("tv-trakt-99999", TvLifecycleState.SourceRemoved)
            ]));
        WatchlistQuery query = new(
            WatchlistCollection.Tv,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending,
            state);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(expectedId);
        result[0].Source.Should().Be("trakt");
        result[0].AvailabilityStatus.Should().Be("unknown_match");
        result[0].Tv.Should().NotBeNull();
    }

    [Fact]
    public async Task GetItemDetailsAsync_WhenPublishedTvExists_MapsTvDetails()
    {
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository([]),
            new StubTvShowReadRepository([CreateTvShow("tv-trakt-12345", TvLifecycleState.Active)]));

        WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("tv-trakt-12345", CancellationToken.None);

        result.Should().NotBeNull();
        result!.MediaType.Should().Be("tv");
        result.Source.Should().Be("trakt");
        result.Tv.Should().NotBeNull();
        result.Tv!.Destinations.SonarrState.Should().Be("unknown");
        result.Tv.Seasons.Should().ContainSingle();
    }

    [Fact]
    public async Task GetItemAsync_WhenLegacyTvIdExistsOnlyInMovieRepository_ReturnsNull()
    {
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository([CreateItem("legacy-tv", MediaType.TvShow, "Legacy")]),
            new StubTvShowReadRepository([]));

        WatchlistItemDto? result = await service.GetItemAsync("legacy-tv", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetItemsAsync_WhenCollectionMovie_ReturnsOnlyMovies()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex),
            CreateItem("tv-1", MediaType.TvShow, "Severance", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.Movie,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("movie-1");
        result[0].MediaType.Should().Be("movie");
    }

    [Fact]
    public async Task GetItemsAsync_WhenCollectionTv_ReturnsOnlyTvShows()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository(items),
            new StubTvShowReadRepository([CreateTvShow("tv-trakt-1", TvLifecycleState.Active)]));
        WatchlistQuery query = new(
            WatchlistCollection.Tv,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("tv-trakt-1");
        result[0].MediaType.Should().Be("tv");
    }

    [Fact]
    public async Task GetItemsAsync_WhenAvailabilityHasSplitReasons_ReturnsOnlyRequestedStates()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("available", MediaType.Movie, "Available", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex),
            CreateItem("missing", MediaType.Movie, "Missing", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.NotOnPlex),
            CreateItem("uncertain", MediaType.TvShow, "Uncertain", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"), AvailabilityStatus.UnknownMatch)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.AvailabilityStatus)
            .Should()
            .OnlyContain(status => status == "available_on_plex" || status == "unknown_match");
    }

    [Fact]
    public async Task GetItemsAsync_WhenSortAddedDescending_PreservesRepositoryOrderForEqualAddedAt()
    {
        DateTimeOffset latestAddedAt = DateTimeOffset.Parse("2026-05-22T10:00:00+02:00");
        DateTimeOffset olderAddedAt = DateTimeOffset.Parse("2026-05-20T10:00:00+02:00");
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("tie-first", MediaType.Movie, "First", latestAddedAt, AvailabilityStatus.AvailableOnPlex),
            CreateItem("older", MediaType.Movie, "Older", olderAddedAt, AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository(items),
            new StubTvShowReadRepository(
                [CreateTvShow("tv-trakt-2", TvLifecycleState.Active) with { AddedAt = latestAddedAt, Title = "Second" }]));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Id).Should().Equal("tie-first", "tv-trakt-2", "older");
    }

    [Fact]
    public async Task GetItemsAsync_WhenSortTitleAscending_SortsCaseInsensitively()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("future", MediaType.Movie, "future Movie", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.Unreleased),
            CreateItem("dune", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(
            new StubWatchlistReadRepository(items),
            new StubTvShowReadRepository([CreateTvShow("tv-trakt-3", TvLifecycleState.Active) with { Title = "Andor" }]));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus>
            {
                AvailabilityStatus.AvailableOnPlex,
                AvailabilityStatus.NotOnPlex,
                AvailabilityStatus.Unreleased,
                AvailabilityStatus.UnknownMatch
            },
            WatchlistSort.TitleAscending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Andor", "Dune: Part Two", "future Movie");
    }

    [Fact]
    public async Task GetItemAsync_WhenItemExists_ReturnsItem()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        WatchlistItemDto? result = await service.GetItemAsync("movie-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("movie-1");
        result.MediaType.Should().Be("movie");
        result.Source.Should().Be("letterboxd");
        result.SourceId.Should().Be("source-movie-1");
        result.Title.Should().Be("Alien");
        result.Year.Should().Be(1979);
        result.Overview.Should().Be("A test overview.");
        result.PosterUrl.Should().Be("https://example.test/poster.jpg");
        result.BackdropUrl.Should().Be("https://example.test/backdrop.jpg");
        result.ReleaseStatus.Should().Be("released");
        result.AvailabilityStatus.Should().Be("available_on_plex");
        result.VodReleaseKnown.Should().BeTrue();
        result.ReleasedOnVod.Should().BeTrue();
        result.VodRegions.Should().Equal("PL", "US");
        result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video", "Max");
        result.AddedAt.Should().Be(AddedAt);
        result.UpdatedAt.Should().Be(UpdatedAt);
    }

    [Fact]
    public async Task GetItemDetailsAsync_WhenItemExists_ReturnsDetailOnlyFieldsAndPrimaryAction()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex) with
            {
                Genres = ["Horror", "Science Fiction"],
                RuntimeMinutes = 117,
                OriginalLanguage = "en",
                TmdbVoteAverage = 8.2,
                TmdbVoteCount = 15000
            }
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("movie-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("movie-1");
        result.Genres.Should().Equal("Horror", "Science Fiction");
        result.RuntimeMinutes.Should().Be(117);
        result.OriginalLanguage.Should().Be("en");
        result.TmdbVoteAverage.Should().Be(8.2);
        result.TmdbVoteCount.Should().Be(15000);
        result.PrimaryActionLabel.Should().Be("Open in Plex");
        result.PrimaryActionEnabled.Should().BeTrue();
        result.PrimaryActionTarget.Should().BeNull();
    }

    [Theory]
    [InlineData(AvailabilityStatus.AvailableOnPlex, "Open in Plex", true)]
    [InlineData(AvailabilityStatus.NotOnPlex, "Unavailable", false)]
    [InlineData(AvailabilityStatus.Unreleased, "Not released", false)]
    [InlineData(AvailabilityStatus.UnknownMatch, "Match uncertain", false)]
    public async Task GetItemDetailsAsync_MapsPrimaryActionFromAvailability(
        AvailabilityStatus availability,
        string label,
        bool enabled)
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", availability)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("movie-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.PrimaryActionLabel.Should().Be(label);
        result.PrimaryActionEnabled.Should().Be(enabled);
        result.PrimaryActionTarget.Should().BeNull();
    }

    [Fact]
    public async Task GetItemDetailsAsync_WhenItemMissing_ReturnsNull()
    {
        WatchlistQueryService service = new(new StubWatchlistReadRepository([]));

        WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("missing", CancellationToken.None);

        result.Should().BeNull();
    }

    private static WatchlistItem CreateItem(
        string id,
        MediaType mediaType,
        string title,
        AvailabilityStatus availabilityStatus = AvailabilityStatus.NotOnPlex)
    {
        return CreateItem(id, mediaType, title, AddedAt, availabilityStatus);
    }

    private static WatchlistItem CreateItem(
        string id,
        MediaType mediaType,
        string title,
        DateTimeOffset addedAt,
        AvailabilityStatus availabilityStatus = AvailabilityStatus.NotOnPlex)
    {
        return new WatchlistItem(
            id,
            mediaType,
            WatchlistSource.Letterboxd,
            $"source-{id}",
            title,
            1979,
            "A test overview.",
            "https://example.test/poster.jpg",
            "https://example.test/backdrop.jpg",
            ReleaseStatus.Released,
            availabilityStatus,
            addedAt,
            UpdatedAt)
        {
            VodReleaseKnown = true,
            ReleasedOnVod = true,
            VodRegions = ["PL", "US"],
            OwnedServiceAvailability = ["Amazon Prime Video", "Max"],
            Genres = ["Drama"],
            RuntimeMinutes = 93,
            OriginalLanguage = "en",
            TmdbVoteAverage = 7.7,
            TmdbVoteCount = 100
        };
    }

    [Fact]
    public async Task GetItemsAsync_WhenAvailabilityPlex_IncludesUnmatchedPlexMovies()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("available-watchlist", MediaType.Movie, "Available Watchlist", AvailabilityStatus.AvailableOnPlex),
            CreateItem("missing-watchlist", MediaType.Movie, "Missing Watchlist", AvailabilityStatus.NotOnPlex)
        ];
        StubPlexMovieInventoryRepository plexRepository = new(
            unmatchedMovies:
            [
                new PlexMovieDto(
                    "bond-1",
                    "Dr. No",
                    1962,
                    "1",
                    "Filmy",
                    "plex://movie/bond-1",
                    "tt0055928",
                646,
                null,
                DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
                "Bond summary.",
                "/library/metadata/bond-1/thumb/1",
                "/library/metadata/bond-1/art/1")
            ]);
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items), plexRepository);
        WatchlistQuery query = new(
            WatchlistCollection.Movie,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.TitleAscending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Available Watchlist", "Dr. No");
        WatchlistItemDto plexOnly = result.Single(item => item.Id == "plex-movie-bond-1");
        plexOnly.Source.Should().Be("plex");
        plexOnly.SourceId.Should().Be("bond-1");
        plexOnly.AvailabilityStatus.Should().Be("available_on_plex");
        plexOnly.LibraryMembership.Should().Be("plex_only");
        plexOnly.Overview.Should().Be("Bond summary.");
        plexOnly.PosterUrl.Should().Be("/api/images/plex/bond-1/poster");
        plexOnly.BackdropUrl.Should().Be("/api/images/plex/bond-1/backdrop");
    }

    [Fact]
    public async Task GetItemsAsync_WhenAvailabilityDoesNotIncludePlex_ExcludesUnmatchedPlexMovies()
    {
        StubPlexMovieInventoryRepository plexRepository = new(
            unmatchedMovies:
            [
                new PlexMovieDto(
                    "bond-1",
                    "Dr. No",
                    1962,
                    "1",
                    "Filmy",
                    "plex://movie/bond-1",
                    "tt0055928",
                646,
                null,
                DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
                "Bond summary.",
                "/library/metadata/bond-1/thumb/1",
                "/library/metadata/bond-1/art/1")
            ]);
        WatchlistQueryService service = new(new StubWatchlistReadRepository([]), plexRepository);
        WatchlistQuery query = new(
            WatchlistCollection.Movie,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.NotOnPlex },
            WatchlistSort.TitleAscending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemDetailsAsync_WhenPlexOnlyMovieExists_ReturnsPlexOnlyDetails()
    {
        StubPlexMovieInventoryRepository plexRepository = new(
            unmatchedMovies:
            [
                new PlexMovieDto(
                    "bond-1",
                    "Dr. No",
                    1962,
                    "1",
                    "Filmy",
                    "plex://movie/bond-1",
                    "tt0055928",
                646,
                null,
                DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
                "Bond summary.",
                "/library/metadata/bond-1/thumb/1",
                "/library/metadata/bond-1/art/1")
            ]);
        WatchlistQueryService service = new(new StubWatchlistReadRepository([]), plexRepository);

        WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("plex-movie-bond-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Dr. No");
        result.Overview.Should().Be("Bond summary.");
        result.PosterUrl.Should().Be("/api/images/plex/bond-1/poster");
        result.BackdropUrl.Should().Be("/api/images/plex/bond-1/backdrop");
        result.Source.Should().Be("plex");
        result.LibraryMembership.Should().Be("plex_only");
        result.PrimaryActionLabel.Should().Be("Unavailable");
        result.PrimaryActionEnabled.Should().BeFalse();
    }

    private sealed class StubWatchlistReadRepository(IReadOnlyList<WatchlistItem> items) : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }
    }

    private sealed class StubTvShowReadRepository(IReadOnlyList<TvShow> shows) : ITvShowReadRepository
    {
        private readonly PublishedTvGeneration generation = new(
            new TvGenerationManifest(
                "tv-generation",
                null,
                TvGenerationKind.ScheduledFull,
                DateTimeOffset.Parse("2026-07-19T11:59:00Z"),
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                new TraktActivityCursor(DateTimeOffset.Parse("2026-07-19T11:00:00Z"), DateTimeOffset.Parse("2026-07-19T11:00:00Z")),
                1,
                shows.Count,
                1,
                shows.Count,
                "v1",
                new Dictionary<string, string>(),
                "membership",
                "progress",
                null,
                null,
                null,
                "valid",
                [],
                [],
                [],
                false,
                ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"],
                []),
            shows);

        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<PublishedTvGeneration?>(generation);
        }

        public Task<TvShow?> GetPublishedShowAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult(generation.Shows.SingleOrDefault(show => show.Id == id));
        }
    }

    private static TvShow CreateTvShow(string id, TvLifecycleState lifecycleState)
    {
        long traktId = long.Parse(id["tv-trakt-".Length..], System.Globalization.CultureInfo.InvariantCulture);
        TvEpisodeProgress episode = new(
            555,
            777,
            1,
            1,
            "The First Episode",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            false,
            null);
        TvProviderAvailability availability = new(
            TvProviderState.Available,
            "PL",
            DateTimeOffset.Parse("2026-07-18T00:00:00Z"),
            "https://www.themoviedb.org/tv/123/watch?locale=PL",
            [new TvProviderOffer(8, "Netflix", TvProviderCategory.Flatrate, "https://image.tmdb.org/t/p/w500/logo.png")]);

        return new TvShow(
            id,
            traktId,
            123,
            456,
            "tt1234567",
            TvIdentityStatus.Verified,
            "Example TV Show",
            2026,
            "A TV overview.",
            "https://image.tmdb.org/t/p/w500/poster.png",
            "https://image.tmdb.org/t/p/w1280/backdrop.png",
            "returning series",
            lifecycleState != TvLifecycleState.RetiredTerminal,
            1,
            0,
            null,
            episode,
            [new TvSeasonProgress(1, 1, 0, false, availability, [episode])],
            [],
            availability,
            lifecycleState,
            lifecycleState == TvLifecycleState.Active ? null : "lifecycle_event",
            1,
            0,
            AddedAt,
            UpdatedAt,
            UpdatedAt,
            "tv-generation",
            null);
    }

    private sealed class StubPlexMovieInventoryRepository(
        IReadOnlyList<PlexMovieDto>? unmatchedMovies = null) : IPlexMovieInventoryRepository
    {
        private readonly IReadOnlyList<PlexMovieDto> unmatchedMovies = unmatchedMovies ?? [];

        public Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
            IReadOnlyList<PlexMovieDto> movies,
            IReadOnlySet<string> scannedSectionKeys,
            DateTimeOffset syncTime,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlexInventoryApplyResult(0, 0));
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlexMovieDto>>([]);
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetUnmatchedMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(unmatchedMovies);
        }

        public Task<PlexMovieDto?> GetMovieAsync(string ratingKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(unmatchedMovies.FirstOrDefault(movie => movie.RatingKey == ratingKey));
        }

        public Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItemWriteModel>>([]);
        }

        public Task ApplyMatchUpdatesAsync(
            IReadOnlyList<PlexMovieMatchUpdate> updates,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
