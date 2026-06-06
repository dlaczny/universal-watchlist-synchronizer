using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbTvWatchlistSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_WithEnrichedItems_CreatesTvShowRecords()
    {
        FakeTvWatchlistClient watchlistClient = new([
            new TmdbTvWatchlistItemDto(1399, "Game of Thrones", "Game of Thrones",
                "Nine noble families fight for control.", "2011-04-17", null, null, "en", 8.5, 25000)
        ]);
        FakeTvMetadataClient metadataClient = new()
        {
            MetadataById =
            {
                [1399] = new TmdbTvMetadataDto(1399, "Game of Thrones", "Game of Thrones",
                    "Nine noble families fight for control.", "2011-04-17", "Ended", null, null, null, null,
                    ["Drama"], "en", 8.5, 25000, new TmdbTvExternalIdsDto("tt0944947", 121361))
            }
        };
        FakeTvWriteRepository repository = new();
        TmdbTvWatchlistSyncService service = CreateService(watchlistClient, metadataClient, repository);

        TmdbTvSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.ItemsFetched.Should().Be(1);
        result.ItemsUpserted.Should().Be(1);
        result.ItemsDeleted.Should().Be(0);
        result.ItemsEnriched.Should().Be(1);
        result.ItemsNotFound.Should().Be(0);
        result.ItemsFailed.Should().Be(0);

        WatchlistItemWriteModel writeModel = repository.WrittenItems.Should().ContainSingle().Subject;
        writeModel.Item.Id.Should().Be("tv-tmdb-1399");
        writeModel.Item.MediaType.Should().Be(MediaType.TvShow);
        writeModel.Item.Source.Should().Be(WatchlistSource.Tmdb);
        writeModel.Item.SourceId.Should().Be("1399");
        writeModel.Item.Title.Should().Be("Game of Thrones");
        writeModel.Item.Year.Should().Be(2011);
        writeModel.Item.ReleaseStatus.Should().Be(ReleaseStatus.Released);
        writeModel.Item.AvailabilityStatus.Should().Be(AvailabilityStatus.NotOnPlex);
        writeModel.ImdbId.Should().Be("tt0944947");
        writeModel.TmdbId.Should().Be(1399);
    }

    [Fact]
    public async Task SyncAsync_WithFutureFirstAirDate_CreatesUnreleasedRecord()
    {
        FakeTvWatchlistClient watchlistClient = new([
            new TmdbTvWatchlistItemDto(1399, "Future Show", "Future Show",
                null, "2099-12-01", null, null, null, null, null)
        ]);
        FakeTvMetadataClient metadataClient = new()
        {
            MetadataById =
            {
                [1399] = new TmdbTvMetadataDto(1399, "Future Show", "Future Show",
                    null, "2099-12-01", "Returning Series", null, null, null, null,
                    [], null, null, null, new TmdbTvExternalIdsDto(null, null))
            }
        };
        FakeTvWriteRepository repository = new();
        TmdbTvWatchlistSyncService service = CreateService(watchlistClient, metadataClient, repository);

        await service.SyncAsync(CancellationToken.None);

        WatchlistItemWriteModel writeModel = repository.WrittenItems.Should().ContainSingle().Subject;
        writeModel.Item.ReleaseStatus.Should().Be(ReleaseStatus.Unreleased);
        writeModel.Item.AvailabilityStatus.Should().Be(AvailabilityStatus.Unreleased);
    }

    [Fact]
    public async Task SyncAsync_WithMissingReleaseState_CreatesUnknownRecord()
    {
        FakeTvWatchlistClient watchlistClient = new([
            new TmdbTvWatchlistItemDto(1399, "Unknown Show", "Unknown Show",
                null, null, null, null, null, null, null)
        ]);
        FakeTvMetadataClient metadataClient = new()
        {
            MetadataById =
            {
                [1399] = new TmdbTvMetadataDto(1399, "Unknown Show", "Unknown Show",
                    null, null, null, null, null, null, null,
                    [], null, null, null, new TmdbTvExternalIdsDto(null, null))
            }
        };
        FakeTvWriteRepository repository = new();
        TmdbTvWatchlistSyncService service = CreateService(watchlistClient, metadataClient, repository);

        await service.SyncAsync(CancellationToken.None);

        WatchlistItemWriteModel writeModel = repository.WrittenItems.Should().ContainSingle().Subject;
        writeModel.Item.ReleaseStatus.Should().Be(ReleaseStatus.Unknown);
        writeModel.Item.AvailabilityStatus.Should().Be(AvailabilityStatus.UnknownMatch);
    }

    [Fact]
    public async Task SyncAsync_WithOneMetadataNotFound_ContinuesAndReportsNotFound()
    {
        FakeTvWatchlistClient watchlistClient = new([
            new TmdbTvWatchlistItemDto(1399, "Found Show", "Found Show", null, null, null, null, null, null, null),
            new TmdbTvWatchlistItemDto(2999, "Missing Show", "Missing Show", null, null, null, null, null, null, null)
        ]);
        FakeTvMetadataClient metadataClient = new()
        {
            MetadataById =
            {
                [1399] = new TmdbTvMetadataDto(1399, "Found Show", "Found Show",
                    null, null, null, null, null, null, null,
                    [], null, null, null, new TmdbTvExternalIdsDto(null, null))
            }
        };
        metadataClient.ExceptionsById[2999] = new TmdbTvNotFoundException("not found");
        FakeTvWriteRepository repository = new();
        TmdbTvWatchlistSyncService service = CreateService(watchlistClient, metadataClient, repository);

        TmdbTvSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.ItemsFetched.Should().Be(2);
        result.ItemsUpserted.Should().Be(1);
        result.ItemsNotFound.Should().Be(1);
        result.ItemsFailed.Should().Be(0);
        repository.WrittenItems.Should().ContainSingle();
    }

    [Fact]
    public async Task SyncAsync_WithOneDependencyFailure_ContinuesAndReportsPartial()
    {
        FakeTvWatchlistClient watchlistClient = new([
            new TmdbTvWatchlistItemDto(1399, "Good Show", "Good Show", null, null, null, null, null, null, null),
            new TmdbTvWatchlistItemDto(2999, "Bad Show", "Bad Show", null, null, null, null, null, null, null)
        ]);
        FakeTvMetadataClient metadataClient = new()
        {
            MetadataById =
            {
                [1399] = new TmdbTvMetadataDto(1399, "Good Show", "Good Show",
                    null, null, null, null, null, null, null,
                    [], null, null, null, new TmdbTvExternalIdsDto(null, null))
            }
        };
        metadataClient.ExceptionsById[2999] = new TmdbUnavailableException("TMDB error");
        FakeTvWriteRepository repository = new();
        TmdbTvWatchlistSyncService service = CreateService(watchlistClient, metadataClient, repository);

        TmdbTvSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        result.Status.Should().Be("partial");
        result.ItemsFetched.Should().Be(2);
        result.ItemsUpserted.Should().Be(1);
        result.ItemsFailed.Should().Be(1);
        repository.WrittenItems.Should().ContainSingle();
    }

    private static TmdbTvWatchlistSyncService CreateService(
        ITmdbTvWatchlistClient watchlistClient,
        ITmdbTvMetadataClient metadataClient,
        IWatchlistWriteRepository repository)
    {
        return new TmdbTvWatchlistSyncService(
            watchlistClient,
            metadataClient,
            repository,
            TimeProvider.System);
    }

    private sealed class FakeTvWriteRepository : IWatchlistWriteRepository
    {
        public List<WatchlistItemWriteModel> WrittenItems { get; } = [];
        public IReadOnlySet<string>? LastSourceIds { get; private set; }

        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItem>>([]);
        }

        public Task<int> ApplyLetterboxdMovieSyncAsync(
            IReadOnlyList<WatchlistItemWriteModel> items,
            IReadOnlySet<string> sourceIds,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<TmdbTvWatchlistApplyResult> ApplyTmdbTvWatchlistSyncAsync(
            IReadOnlyList<WatchlistItemWriteModel> items,
            IReadOnlySet<string> sourceIds,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            WrittenItems.AddRange(items);
            LastSourceIds = sourceIds;
            return Task.FromResult(new TmdbTvWatchlistApplyResult(items.Count, 0));
        }
    }

    private sealed class FakeTvWatchlistClient(IReadOnlyList<TmdbTvWatchlistItemDto> items) : ITmdbTvWatchlistClient
    {
        public Task<IReadOnlyList<TmdbTvWatchlistItemDto>> GetWatchlistAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }
    }

    private sealed class FakeTvMetadataClient : ITmdbTvMetadataClient
    {
        public Dictionary<int, TmdbTvMetadataDto> MetadataById { get; } = [];
        public Dictionary<int, Exception> ExceptionsById { get; } = [];

        public Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken)
        {
            if (ExceptionsById.TryGetValue(tmdbId, out Exception? exception))
            {
                throw exception;
            }

            return Task.FromResult(MetadataById[tmdbId]);
        }
    }
}
