using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTmdbProviderCatalogRepositoryTests : IAsyncLifetime
{
    private const string CollectionName = "tmdb_provider_catalog";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTmdbProviderCatalogRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            TmdbProviderCatalogCollectionName = CollectionName
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task ReplaceAsync_RoundTripsSingletonSnapshotWithExactFetchTimesAndMembership()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        TmdbProviderCatalogSnapshot snapshot = Snapshot(
            Now.AddMinutes(-2),
            Now.AddMinutes(-1));

        await repository.ReplaceAsync(snapshot, CancellationToken.None);

        TmdbProviderCatalogSnapshot? result = await repository.GetAsync(CancellationToken.None);
        result.Should().BeEquivalentTo(snapshot, options => options.WithStrictOrdering());
        result!.Providers.Should().Equal(snapshot.Providers);
        result.RegionCodes.Should().Equal("PL", "DE");
        IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(CollectionName);
        BsonDocument raw = await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        raw["_id"].AsString.Should().Be("singleton");
        raw["catalogFetchedAt"].Should().NotBe(BsonNull.Value);
        raw["regionsFetchedAt"].Should().NotBe(BsonNull.Value);
        raw["stale"].AsBoolean.Should().BeFalse();
        raw["lastErrorCode"].Should().Be(BsonNull.Value);
        raw["lastErrorAt"].Should().Be(BsonNull.Value);
    }

    [Fact]
    public async Task GetAsync_WhenSingletonMissing_ReturnsNull()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();

        TmdbProviderCatalogSnapshot? result = await repository.GetAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkStaleAsync_WithoutPayloadPersistsOnlyDurableFailureAttempt()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();

        await repository.MarkStaleAsync(
            "tmdb_unavailable",
            Now,
            CancellationToken.None);

        (await repository.GetAsync(CancellationToken.None)).Should().BeNull();
        (await repository.GetLastAttemptAtAsync(CancellationToken.None)).Should().Be(Now);
        BsonDocument raw = await ReadRawAsync();
        raw["lastErrorCode"].AsString.Should().Be("tmdb_unavailable");
        raw.Contains("providers").Should().BeFalse();
        raw.Contains("regionCodes").Should().BeFalse();
    }

    [Fact]
    public async Task MarkStaleAsync_AtomicallyChangesOnlyHealthAndPreservesPriorPayloadAndFetchTimes()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        TmdbProviderCatalogSnapshot snapshot = Snapshot(
            Now.AddDays(-2),
            Now.AddDays(-2).AddMinutes(1));
        await repository.ReplaceAsync(snapshot, CancellationToken.None);
        BsonDocument before = await ReadRawAsync();

        await repository.MarkStaleAsync(
            "tmdb_unavailable",
            Now,
            CancellationToken.None);

        TmdbProviderCatalogSnapshot? result = await repository.GetAsync(CancellationToken.None);
        result.Should().BeEquivalentTo(snapshot with
        {
            Stale = true,
            LastErrorCode = "tmdb_unavailable",
            LastErrorAt = Now
        }, options => options.WithStrictOrdering());
        BsonDocument after = await ReadRawAsync();
        after["catalogFetchedAt"].Should().Be(before["catalogFetchedAt"]);
        after["regionsFetchedAt"].Should().Be(before["regionsFetchedAt"]);
        after["providers"].Should().Be(before["providers"]);
        after["regionCodes"].Should().Be(before["regionCodes"]);
    }

    [Fact]
    public async Task ReplaceAsync_AfterFailureAtomicallyClearsStaleErrorHealth()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        await repository.ReplaceAsync(
            Snapshot(Now.AddDays(-2), Now.AddDays(-2)),
            CancellationToken.None);
        await repository.MarkStaleAsync("tmdb_parse_error", Now.AddDays(-1), CancellationToken.None);
        TmdbProviderCatalogSnapshot replacement = Snapshot(Now, Now);

        await repository.ReplaceAsync(replacement, CancellationToken.None);

        TmdbProviderCatalogSnapshot? result = await repository.GetAsync(CancellationToken.None);
        result.Should().BeEquivalentTo(replacement, options => options.WithStrictOrdering());
        result!.Stale.Should().BeFalse();
        result.LastErrorCode.Should().BeNull();
        result.LastErrorAt.Should().BeNull();
        long count = await database.GetCollection<BsonDocument>(CollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        count.Should().Be(1);
    }

    [Fact]
    public async Task ReplaceAsync_SnapshotsCallerCollectionsAndReturnsReadOnlyCollections()
    {
        List<TmdbWatchProviderCatalogEntryDto> providers =
            [new(119, "Max", "/max.jpg", 4)];
        List<string> regions = ["PL"];
        TmdbProviderCatalogSnapshot snapshot = new(
            Now,
            Now,
            false,
            null,
            null,
            providers,
            regions);
        MongoTmdbProviderCatalogRepository repository = CreateRepository();

        await repository.ReplaceAsync(snapshot, CancellationToken.None);
        providers.Add(new TmdbWatchProviderCatalogEntryDto(1899, "Channel", null, 5));
        regions.Add("DE");

        TmdbProviderCatalogSnapshot result = (await repository.GetAsync(
            CancellationToken.None))!;
        result.Providers.Should().ContainSingle();
        result.RegionCodes.Should().Equal("PL");
        Action mutateProviders = () => ((IList<TmdbWatchProviderCatalogEntryDto>)result.Providers)
            .Add(new TmdbWatchProviderCatalogEntryDto(1773, "Sky", null, 6));
        Action mutateRegions = () => ((IList<string>)result.RegionCodes).Add("US");
        mutateProviders.Should().Throw<NotSupportedException>();
        mutateRegions.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task ReplaceAsync_WhenRecordCopyBypassesHealthInvariant_RejectsBeforeWrite()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        TmdbProviderCatalogSnapshot invalid = Snapshot(Now, Now) with { Stale = true };

        Func<Task> action = () => repository.ReplaceAsync(invalid, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        long count = await database.GetCollection<BsonDocument>(CollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ReplaceAsync_WhenRecordCopyBypassesProviderInvariant_RejectsBeforeWrite()
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        TmdbProviderCatalogSnapshot valid = Snapshot(Now, Now);
        TmdbWatchProviderCatalogEntryDto invalidProvider = valid.Providers[0] with
        {
            ProviderId = 0
        };
        TmdbProviderCatalogSnapshot invalid = valid with
        {
            Providers = [invalidProvider]
        };

        Func<Task> action = () => repository.ReplaceAsync(invalid, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        long count = await database.GetCollection<BsonDocument>(CollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        count.Should().Be(0);
    }

    [Theory]
    [InlineData("raw response body")]
    [InlineData("token=secret")]
    [InlineData("TMDB returned HTTP 500.")]
    public async Task MarkStaleAsync_WhenErrorCodeIsNotStableRejectsWithoutMutation(string error)
    {
        MongoTmdbProviderCatalogRepository repository = CreateRepository();
        TmdbProviderCatalogSnapshot snapshot = Snapshot(Now, Now);
        await repository.ReplaceAsync(snapshot, CancellationToken.None);

        Func<Task> action = () => repository.MarkStaleAsync(
            error,
            Now,
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        (await repository.GetAsync(CancellationToken.None)).Should()
            .BeEquivalentTo(snapshot, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task HostedService_WhenCatalogMissing_RefreshesCatalogAndRegionsAsOneSnapshot()
    {
        HostedRepository repository = new(null);
        HostedTmdbClient tmdbClient = new();
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            logger);

        await service.StartAsync(CancellationToken.None);
        await repository.WaitForReplaceAsync().WaitAsync(TimeSpan.FromSeconds(1));

        repository.Current.Should().BeEquivalentTo(new TmdbProviderCatalogSnapshot(
            Now,
            Now,
            false,
            null,
            null,
            tmdbClient.Catalog.Providers,
            tmdbClient.Regions.RegionCodes), options => options.WithStrictOrdering());
        tmdbClient.CatalogCalls.Should().Be(1);
        tmdbClient.RegionCalls.Should().Be(1);
        logger.Messages.Should().BeEmpty();
        ManualTimer timer = await timeProvider.WaitForTimerAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));
        timer.DueTime.Should().Be(TimeSpan.FromDays(1));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_WhenRefreshFails_RetainsPayloadMarksStableErrorAndRedactsLogs()
    {
        TmdbProviderCatalogSnapshot prior = Snapshot(Now.AddDays(-2), Now.AddDays(-2));
        HostedRepository repository = new(prior);
        string sentinel = "token-and-response-body-sentinel";
        HostedTmdbClient tmdbClient = new()
        {
            CatalogException = new TmdbUnavailableException(sentinel)
        };
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            logger);

        await service.StartAsync(CancellationToken.None);
        await repository.WaitForMarkStaleAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await logger.WaitForMessageAsync().WaitAsync(TimeSpan.FromSeconds(1));

        repository.Current.Should().Be(prior with
        {
            Stale = true,
            LastErrorCode = "tmdb_unavailable",
            LastErrorAt = Now
        });
        repository.ReplaceCalls.Should().Be(0);
        logger.Messages.Should().ContainSingle().Which.Should().Contain("tmdb_unavailable");
        string logs = string.Join(Environment.NewLine, logger.Messages);
        logs.Should().NotContain(sentinel);
        logs.Should().NotContain("TmdbUnavailableException");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_WhenRegionsFailAfterCatalogRead_DoesNotPublishHalfSnapshot()
    {
        TmdbProviderCatalogSnapshot prior = Snapshot(Now.AddDays(-2), Now.AddDays(-2));
        HostedRepository repository = new(prior);
        HostedTmdbClient tmdbClient = new()
        {
            RegionsException = new TmdbParseException("fixed parse failure")
        };
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            logger);

        await service.StartAsync(CancellationToken.None);
        await repository.WaitForMarkStaleAsync().WaitAsync(TimeSpan.FromSeconds(1));

        repository.ReplaceCalls.Should().Be(0);
        repository.Current!.Providers.Should().Equal(prior.Providers);
        repository.Current.RegionCodes.Should().Equal(prior.RegionCodes);
        repository.Current.CatalogFetchedAt.Should().Be(prior.CatalogFetchedAt);
        repository.Current.RegionsFetchedAt.Should().Be(prior.RegionsFetchedAt);
        repository.Current.Stale.Should().BeTrue();
        repository.Current.LastErrorCode.Should().Be("tmdb_parse_error");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_WhenPriorRefreshIsFresh_WaitsUntilDurableDailyDueTime()
    {
        TmdbProviderCatalogSnapshot prior = Snapshot(Now.AddHours(-2), Now.AddHours(-1));
        HostedRepository repository = new(prior);
        HostedTmdbClient tmdbClient = new();
        ManualTimeProvider timeProvider = new(Now);
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            new CapturingLogger());

        await service.StartAsync(CancellationToken.None);
        ManualTimer timer = await timeProvider.WaitForTimerAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        tmdbClient.CatalogCalls.Should().Be(0);
        tmdbClient.RegionCalls.Should().Be(0);
        timer.DueTime.Should().Be(TimeSpan.FromHours(23));
        timeProvider.Advance(TimeSpan.FromHours(23));
        timer.Fire();
        await repository.WaitForReplaceAsync().WaitAsync(TimeSpan.FromSeconds(1));
        tmdbClient.CatalogCalls.Should().Be(1);
        tmdbClient.RegionCalls.Should().Be(1);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_AfterRecentFailedAttempt_DoesNotHammerOnRestart()
    {
        TmdbProviderCatalogSnapshot prior = Snapshot(Now.AddDays(-2), Now.AddDays(-2)) with
        {
            Stale = true,
            LastErrorCode = "tmdb_unavailable",
            LastErrorAt = Now.AddHours(-1)
        };
        HostedRepository repository = new(prior);
        HostedTmdbClient tmdbClient = new();
        ManualTimeProvider timeProvider = new(Now);
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            new CapturingLogger());

        await service.StartAsync(CancellationToken.None);
        ManualTimer timer = await timeProvider.WaitForTimerAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        timer.DueTime.Should().Be(TimeSpan.FromHours(23));
        tmdbClient.CatalogCalls.Should().Be(0);
        repository.MarkStaleCalls.Should().Be(0);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_InitialFailureAttemptPreventsRestartHammeringWithoutPayload()
    {
        HostedRepository repository = new(null);
        HostedTmdbClient failingClient = new()
        {
            CatalogException = new TmdbUnavailableException("failure")
        };
        ManualTimeProvider firstTime = new(Now);
        TmdbProviderCatalogHostedService first = new(
            failingClient,
            repository,
            firstTime,
            new CapturingLogger());
        await first.StartAsync(CancellationToken.None);
        await repository.WaitForMarkStaleAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await first.StopAsync(CancellationToken.None);

        HostedTmdbClient restartClient = new();
        ManualTimeProvider restartTime = new(Now.AddHours(1));
        TmdbProviderCatalogHostedService restarted = new(
            restartClient,
            repository,
            restartTime,
            new CapturingLogger());
        await restarted.StartAsync(CancellationToken.None);
        ManualTimer timer = await restartTime.WaitForTimerAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        timer.DueTime.Should().Be(TimeSpan.FromHours(23));
        restartClient.CatalogCalls.Should().Be(0);
        restartClient.RegionCalls.Should().Be(0);
        await restarted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopDuringDailyDelay_DoesNotMarkStaleOrLogFailure()
    {
        HostedRepository repository = new(Snapshot(Now, Now));
        HostedTmdbClient tmdbClient = new();
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            logger);
        await service.StartAsync(CancellationToken.None);
        await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

        repository.MarkStaleCalls.Should().Be(0);
        repository.ReplaceCalls.Should().Be(0);
        logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task HostedService_StopDuringInFlightClientCall_WithUnrelatedCancellationToken_IsNeutral()
    {
        HostedRepository repository = new(null);
        HostedTmdbClient tmdbClient = new()
        {
            CancelCatalogWithUnrelatedToken = true
        };
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TmdbProviderCatalogHostedService service = new(
            tmdbClient,
            repository,
            timeProvider,
            logger);
        await service.StartAsync(CancellationToken.None);
        await tmdbClient.WaitForCatalogStartAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Func<Task> stop = () => service.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync();
        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
        repository.MarkStaleCalls.Should().Be(0);
        repository.ReplaceCalls.Should().Be(0);
        logger.Messages.Should().BeEmpty();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoTmdbProviderCatalogRepository CreateRepository()
    {
        return new MongoTmdbProviderCatalogRepository(database, Options.Create(options));
    }

    private async Task<BsonDocument> ReadRawAsync()
    {
        return await database.GetCollection<BsonDocument>(CollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .SingleAsync();
    }

    private static TmdbProviderCatalogSnapshot Snapshot(
        DateTimeOffset catalogFetchedAt,
        DateTimeOffset regionsFetchedAt)
    {
        return new TmdbProviderCatalogSnapshot(
            catalogFetchedAt,
            regionsFetchedAt,
            false,
            null,
            null,
            [
                new TmdbWatchProviderCatalogEntryDto(119, "Max", "/max.jpg", 4),
                new TmdbWatchProviderCatalogEntryDto(1899, "Channel", "/channel.jpg", 12)
            ],
            ["PL", "DE"]);
    }

    private sealed class HostedRepository(TmdbProviderCatalogSnapshot? current)
        : ITmdbProviderCatalogRepository
    {
        private TaskCompletionSource replaceSignal = NewSignal();
        private TaskCompletionSource staleSignal = NewSignal();
        private DateTimeOffset? lastAttemptAt = GetLatestAttempt(current);

        public TmdbProviderCatalogSnapshot? Current { get; private set; } = current;

        public int ReplaceCalls { get; private set; }

        public int MarkStaleCalls { get; private set; }

        public Task<TmdbProviderCatalogSnapshot?> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Current);
        }

        public Task<DateTimeOffset?> GetLastAttemptAtAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(lastAttemptAt);
        }

        public Task ReplaceAsync(
            TmdbProviderCatalogSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            Current = snapshot;
            lastAttemptAt = GetLatestAttempt(snapshot);
            ReplaceCalls++;
            replaceSignal.TrySetResult();
            return Task.CompletedTask;
        }

        public Task MarkStaleAsync(
            string errorCode,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken)
        {
            if (Current is not null)
            {
                Current = Current with
                {
                    Stale = true,
                    LastErrorCode = errorCode,
                    LastErrorAt = failedAt
                };
            }

            lastAttemptAt = failedAt;
            MarkStaleCalls++;
            staleSignal.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForReplaceAsync() => replaceSignal.Task;

        public Task WaitForMarkStaleAsync() => staleSignal.Task;

        private static TaskCompletionSource NewSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static DateTimeOffset? GetLatestAttempt(TmdbProviderCatalogSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return null;
            }

            DateTimeOffset latestFetch = snapshot.CatalogFetchedAt > snapshot.RegionsFetchedAt
                ? snapshot.CatalogFetchedAt
                : snapshot.RegionsFetchedAt;
            return snapshot.LastErrorAt is DateTimeOffset errorAt && errorAt > latestFetch
                ? errorAt
                : latestFetch;
        }
    }

    private sealed class HostedTmdbClient : ITmdbTvMetadataClient
    {
        private readonly TaskCompletionSource catalogStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TmdbWatchProviderCatalogDto Catalog { get; } = new(
            Now,
            [new TmdbWatchProviderCatalogEntryDto(119, "Max", "/max.jpg", 4)]);

        public TmdbWatchProviderRegionsDto Regions { get; } = new(Now, ["PL", "DE"]);

        public Exception? CatalogException { get; init; }

        public Exception? RegionsException { get; init; }

        public bool CancelCatalogWithUnrelatedToken { get; init; }

        public int CatalogCalls { get; private set; }

        public int RegionCalls { get; private set; }

        public Task<TmdbTvMetadataDto> GetTvMetadataAsync(
            int tmdbId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TmdbTvProviderDataDto> GetTvProvidersAsync(
            int tmdbId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TmdbTvProviderDataDto> GetSeasonProvidersAsync(
            int tmdbId,
            int seasonNumber,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task<TmdbWatchProviderCatalogDto> GetProviderCatalogAsync(
            CancellationToken cancellationToken)
        {
            CatalogCalls++;
            catalogStarted.TrySetResult();
            if (CancelCatalogWithUnrelatedToken)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }
            }

            if (CatalogException is not null)
            {
                throw CatalogException;
            }

            return Catalog;
        }

        public Task WaitForCatalogStartAsync() => catalogStarted.Task;

        public Task<TmdbWatchProviderRegionsDto> GetProviderRegionsAsync(
            CancellationToken cancellationToken)
        {
            RegionCalls++;
            return RegionsException is null
                ? Task.FromResult(Regions)
                : Task.FromException<TmdbWatchProviderRegionsDto>(RegionsException);
        }
    }

    private sealed class CapturingLogger : ILogger<TmdbProviderCatalogHostedService>
    {
        private readonly TaskCompletionSource messageSignal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Messages { get; } = [];

        public Task WaitForMessageAsync() => messageSignal.Task;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            messageSignal.TrySetResult();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object sync = new();
        private readonly Queue<ManualTimer> timers = [];
        private TaskCompletionSource<ManualTimer>? waiter;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ManualTimer timer = new(callback, state, dueTime, period);
            TaskCompletionSource<ManualTimer>? pending;
            lock (sync)
            {
                pending = waiter;
                waiter = null;
                if (pending is null)
                {
                    timers.Enqueue(timer);
                }
            }

            pending?.TrySetResult(timer);
            return timer;
        }

        public Task<ManualTimer> WaitForTimerAsync()
        {
            lock (sync)
            {
                if (timers.Count > 0)
                {
                    return Task.FromResult(timers.Dequeue());
                }

                waiter ??= new TaskCompletionSource<ManualTimer>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return waiter.Task;
            }
        }

        public void Advance(TimeSpan amount)
        {
            utcNow = utcNow.Add(amount);
        }
    }

    private sealed class ManualTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private bool disposed;

        public TimeSpan DueTime { get; } = dueTime;

        public TimeSpan Period { get; } = period;

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod) => !disposed;

        public void Fire()
        {
            if (!disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
