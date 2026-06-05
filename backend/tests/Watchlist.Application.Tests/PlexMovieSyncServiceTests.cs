using FluentAssertions;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class PlexMovieSyncServiceTests
{
    [Fact]
    public async Task SyncMoviesAsync_ScansMovieSectionsStoresInventoryAndAppliesMatches()
    {
        FakePlexClient client = new()
        {
            Sections =
            [
                new PlexLibrarySectionDto("1", "movie", "Filmy"),
                new PlexLibrarySectionDto("2", "show", "Seriale")
            ],
            MoviesBySection =
            {
                ["1"] = [new PlexMovieDto("8058", "10 Things I Hate About You", 1999, "1", "Filmy", "plex://movie/local", "tt0147800", 4951, null)]
            }
        };
        FakePlexRepository repository = new()
        {
            WatchlistMovies =
            [
                CreateWatchlistMovie("4951", "10 Things I Hate About You", 1999, "tt0147800", 4951)
            ]
        };
        PlexMovieSyncService service = new(client, repository, new FakeTimeProvider());

        PlexMovieSyncResultDto result = await service.SyncMoviesAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.SectionsScanned.Should().Be(1);
        result.ItemsFetched.Should().Be(1);
        result.ItemsUpserted.Should().Be(1);
        result.WatchlistItemsMatched.Should().Be(1);
        repository.ScannedSectionKeys.Should().Equal("1");
        repository.MatchUpdates.Should().ContainSingle(update =>
            update.WatchlistItemId == "movie-letterboxd-4951"
            && update.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            && update.PlexRatingKey == "8058"
            && update.PlexMatchReason == "imdb");
    }

    private static WatchlistItemWriteModel CreateWatchlistMovie(
        string sourceId,
        string title,
        int? year,
        string? imdbId,
        int? tmdbId)
    {
        WatchlistItem item = new(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            title,
            year,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"));

        return new WatchlistItemWriteModel(item, imdbId, null, tmdbId);
    }

    private sealed class FakePlexClient : IPlexLibraryClient
    {
        public IReadOnlyList<PlexLibrarySectionDto> Sections { get; init; } = [];

        public Dictionary<string, IReadOnlyList<PlexMovieDto>> MoviesBySection { get; } = [];

        public Task<IReadOnlyList<PlexLibrarySectionDto>> GetSectionsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Sections);
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(
            PlexLibrarySectionDto section,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MoviesBySection[section.Key]);
        }
    }

    private sealed class FakePlexRepository : IPlexMovieInventoryRepository
    {
        public IReadOnlyList<WatchlistItemWriteModel> WatchlistMovies { get; init; } = [];

        public IReadOnlySet<string> ScannedSectionKeys { get; private set; } = new HashSet<string>();

        public List<PlexMovieMatchUpdate> MatchUpdates { get; } = [];

        private IReadOnlyList<PlexMovieDto> movies = [];

        public Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
            IReadOnlyList<PlexMovieDto> sourceMovies,
            IReadOnlySet<string> scannedSectionKeys,
            DateTimeOffset syncTime,
            CancellationToken cancellationToken)
        {
            movies = sourceMovies;
            ScannedSectionKeys = scannedSectionKeys;
            return Task.FromResult(new PlexInventoryApplyResult(sourceMovies.Count, 0));
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(movies);
        }

        public Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(WatchlistMovies);
        }

        public Task ApplyMatchUpdatesAsync(
            IReadOnlyList<PlexMovieMatchUpdate> updates,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            MatchUpdates.AddRange(updates);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return DateTimeOffset.Parse("2026-06-05T12:00:00Z");
        }
    }
}
