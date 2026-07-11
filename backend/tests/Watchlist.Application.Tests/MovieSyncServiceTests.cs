using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class MovieSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-11T08:00:00Z");

    [Fact]
    public async Task SyncAsync_RunsOnlyMovieStagesInOrder()
    {
        List<string> calls = [];
        MovieSyncService service = CreateService(calls, tmdbItemsFailed: 0);

        MovieSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        calls.Should().Equal("letterboxd", "tmdb_movies", "plex_movies");
        result.Status.Should().Be("completed");
        result.Letterboxd.ItemsFetched.Should().Be(2);
        result.TmdbMovies.ItemsEnriched.Should().Be(2);
        result.PlexMovies.WatchlistItemsMatched.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_WhenTmdbEnrichmentHasFailures_ReturnsPartial()
    {
        List<string> calls = [];
        MovieSyncService service = CreateService(calls, tmdbItemsFailed: 1);

        MovieSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        calls.Should().Equal("letterboxd", "tmdb_movies", "plex_movies");
        result.Status.Should().Be("partial");
        result.TmdbMovies.ItemsFailed.Should().Be(1);
    }

    private static MovieSyncService CreateService(List<string> calls, int tmdbItemsFailed)
    {
        return new MovieSyncService(
            new FakeLetterboxd(calls),
            new FakeTmdbMovies(calls, tmdbItemsFailed),
            new FakePlexMovies(calls),
            new StaticTimeProvider());
    }

    private sealed class FakeLetterboxd(List<string> calls) : ILetterboxdMovieSyncService
    {
        public Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
        {
            calls.Add("letterboxd");
            return Task.FromResult(new LetterboxdSyncResultDto(
                "completed",
                Now,
                Now.AddSeconds(1),
                2,
                2,
                0));
        }
    }

    private sealed class FakeTmdbMovies(List<string> calls, int itemsFailed)
        : ITmdbMovieEnrichmentService
    {
        public Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(
            CancellationToken cancellationToken)
        {
            calls.Add("tmdb_movies");
            return Task.FromResult(new TmdbMovieEnrichmentResultDto(
                itemsFailed == 0 ? "completed" : "partial",
                Now.AddSeconds(1),
                Now.AddSeconds(2),
                2,
                2 - itemsFailed,
                0,
                itemsFailed));
        }

        public Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(
            string id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<TmdbSingleMovieEnrichmentResultDto?>(null);
        }
    }

    private sealed class FakePlexMovies(List<string> calls) : IPlexMovieSyncService
    {
        public Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            calls.Add("plex_movies");
            return Task.FromResult(new PlexMovieSyncResultDto(
                "completed",
                Now.AddSeconds(2),
                Now.AddSeconds(3),
                1,
                10,
                10,
                0,
                1,
                1,
                0));
        }
    }

    private sealed class StaticTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
