using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class CombinedSyncServiceTests
{
    [Fact]
    public async Task SyncAllAsync_RunsLetterboxdTmdbAndPlexInOrder()
    {
        List<string> calls = [];
        CombinedSyncService service = new(
            new FakeLetterboxd(calls),
            new FakeTmdb(calls),
            new FakePlex(calls),
            new FakeTimeProvider());

        CombinedSyncResultDto result = await service.SyncAllAsync(CancellationToken.None);

        calls.Should().Equal("letterboxd", "tmdb", "plex");
        result.Status.Should().Be("completed");
        result.Letterboxd.ItemsFetched.Should().Be(2);
        result.TmdbMovies.ItemsEnriched.Should().Be(2);
        result.PlexMovies.WatchlistItemsMatched.Should().Be(1);
    }

    private sealed class FakeLetterboxd(List<string> calls) : ILetterboxdMovieSyncService
    {
        public Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
        {
            calls.Add("letterboxd");
            return Task.FromResult(new LetterboxdSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:00Z"), DateTimeOffset.Parse("2026-06-05T12:00:01Z"), 2, 2, 0));
        }
    }

    private sealed class FakeTmdb(List<string> calls) : ITmdbMovieEnrichmentService
    {
        public Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            calls.Add("tmdb");
            return Task.FromResult(new TmdbMovieEnrichmentResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:01Z"), DateTimeOffset.Parse("2026-06-05T12:00:02Z"), 2, 2, 0, 0));
        }

        public Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult<TmdbSingleMovieEnrichmentResultDto?>(null);
        }
    }

    private sealed class FakePlex(List<string> calls) : IPlexMovieSyncService
    {
        public Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            calls.Add("plex");
            return Task.FromResult(new PlexMovieSyncResultDto("completed", DateTimeOffset.Parse("2026-06-05T12:00:02Z"), DateTimeOffset.Parse("2026-06-05T12:00:03Z"), 1, 1, 1, 0, 1, 1, 0));
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
