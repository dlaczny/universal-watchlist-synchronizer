using FluentAssertions;
using Microsoft.Extensions.Logging;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TvGenerationRetentionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 18, 0, 0, TimeSpan.FromHours(2));
    private static readonly TvGenerationRetentionPolicy Policy = new(
        TimeSpan.FromDays(7),
        48,
        TimeSpan.FromHours(24));
    private const string CurrentGenerationId =
        "tv-20260729160000000-00000000000000000000000000000001";

    [Fact]
    public async Task PruneRequiredAsync_AppliesPlannerResultAndLogsRedactedCounts()
    {
        FakeRepository repository = new()
        {
            Snapshot = CurrentOnlySnapshot()
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        await service.PruneRequiredAsync(CancellationToken.None);

        repository.ReadCalls.Should().Be(1);
        repository.ApplyCalls.Should().Be(1);
        repository.LastPlan.Should().NotBeNull();
        repository.LastPlan!.ExpectedCurrentGenerationId.Should().Be(CurrentGenerationId);
        repository.LastPlan.RetainedGenerationIds.Should().Equal(CurrentGenerationId);
        LogEntry information = logger.Entries.Should().ContainSingle(
            entry => entry.Level == LogLevel.Information).Subject;
        information.Message.Should().Contain("pre_sync");
        information.Message.Should().ContainAll(
            "Retained=",
            "ExpiredManifests=",
            "ExpiredOrphans=",
            "DeferredOrphans=",
            "UncertainOrphans=",
            "ShowDocumentsDeleted=",
            "LifecycleEventsDeleted=",
            "ManifestsDeleted=");
        information.Message.Should().NotContain(CurrentGenerationId);
    }

    [Fact]
    public async Task PruneRequiredAsync_RepositoryReadFailure_ThrowsStableTypedFailure()
    {
        const string secret = "mongodb://admin:read-secret@example";
        InvalidOperationException innerException = new(secret);
        FakeRepository repository = new()
        {
            ReadException = innerException
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneRequiredAsync(CancellationToken.None);

        TvGenerationRetentionException exception = (await action.Should()
            .ThrowAsync<TvGenerationRetentionException>())
            .Which;
        exception.Code.Should().Be(TvGenerationRetentionException.StableCode);
        exception.Message.Should().Be("TV generation retention failed.");
        exception.Message.Should().NotContain(secret);
        exception.InnerException.Should().BeSameAs(innerException);
        repository.ReadCalls.Should().Be(1);
        repository.ApplyCalls.Should().Be(0);
        LogEntry failure = logger.Entries.Should().ContainSingle().Subject;
        AssertRedactedFailureLog(
            failure,
            "pre_sync",
            TvGenerationRetentionException.StableCode,
            nameof(InvalidOperationException),
            secret);
    }

    [Fact]
    public async Task PruneRequiredAsync_RepositoryApplyFailure_ThrowsStableTypedFailure()
    {
        const string secret = "apply-secret-payload";
        IOException innerException = new(secret);
        FakeRepository repository = new()
        {
            Snapshot = CurrentOnlySnapshot(),
            ApplyException = innerException
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneRequiredAsync(CancellationToken.None);

        TvGenerationRetentionException exception = (await action.Should()
            .ThrowAsync<TvGenerationRetentionException>())
            .Which;
        exception.Code.Should().Be(TvGenerationRetentionException.StableCode);
        exception.Message.Should().Be("TV generation retention failed.");
        exception.InnerException.Should().BeSameAs(innerException);
        repository.ApplyCalls.Should().Be(1);
        LogEntry failure = logger.Entries.Should().ContainSingle().Subject;
        AssertRedactedFailureLog(
            failure,
            "pre_sync",
            TvGenerationRetentionException.StableCode,
            nameof(IOException),
            secret);
    }

    [Fact]
    public async Task PruneRequiredAsync_PlannerFailure_ThrowsStableTypedFailure()
    {
        FakeRepository repository = new()
        {
            Snapshot = new TvGenerationRetentionSnapshot(CurrentGenerationId, [], [])
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneRequiredAsync(CancellationToken.None);

        TvGenerationRetentionException exception = (await action.Should()
            .ThrowAsync<TvGenerationRetentionException>())
            .Which;
        exception.Code.Should().Be(TvGenerationRetentionException.StableCode);
        exception.Message.Should().Be("TV generation retention failed.");
        exception.InnerException.Should().BeOfType<InvalidOperationException>();
        repository.ApplyCalls.Should().Be(0);
        LogEntry failure = logger.Entries.Should().ContainSingle().Subject;
        failure.Exception.Should().BeNull();
        failure.Message.Should().ContainAll(
            "pre_sync",
            TvGenerationRetentionException.StableCode,
            nameof(InvalidOperationException));
        failure.StructuredState.Should().Contain(
            pair => pair.Key == "ExceptionType"
                && Equals(pair.Value, nameof(InvalidOperationException)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PruneBestEffortAsync_ReadOrApplyFailure_LogsDeferredAndReturns(
        bool failApply)
    {
        const string secret = "post-publish-secret";
        IOException failure = new(secret);
        FakeRepository repository = new()
        {
            Snapshot = CurrentOnlySnapshot(),
            ReadException = failApply ? null : failure,
            ApplyException = failApply ? failure : null
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneBestEffortAsync(CancellationToken.None);

        await action.Should().NotThrowAsync();
        repository.ReadCalls.Should().Be(1);
        repository.ApplyCalls.Should().Be(failApply ? 1 : 0);
        LogEntry warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        AssertRedactedFailureLog(
            warning,
            "post_publish",
            "tv_generation_retention_deferred",
            nameof(IOException),
            secret);
    }

    [Fact]
    public async Task PruneRequiredAsync_CallerCancellation_PropagatesUnchangedAndIsNotLogged()
    {
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();
        OperationCanceledException cancellation = new(
            "caller-cancelled-secret",
            cancellationSource.Token);
        FakeRepository repository = new()
        {
            ReadException = cancellation
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneRequiredAsync(cancellationSource.Token);

        OperationCanceledException thrown = (await action.Should()
            .ThrowAsync<OperationCanceledException>())
            .Which;
        thrown.Should().BeSameAs(cancellation);
        repository.ApplyCalls.Should().Be(0);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneBestEffortAsync_CallerCancellation_LogsDeferredAndReturns()
    {
        const string secret = "caller-cancelled-secret";
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();
        OperationCanceledException cancellation = new(secret, cancellationSource.Token);
        FakeRepository repository = new()
        {
            ReadException = cancellation
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneBestEffortAsync(cancellationSource.Token);

        await action.Should().NotThrowAsync();
        repository.ReadCalls.Should().Be(1);
        repository.ApplyCalls.Should().Be(0);
        LogEntry warning = logger.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        AssertRedactedFailureLog(
            warning,
            "post_publish",
            "tv_generation_retention_deferred",
            nameof(OperationCanceledException),
            secret);
    }

    [Fact]
    public async Task PruneRequiredAsync_ExistingTypedFailure_IsNotDoubleWrapped()
    {
        TvGenerationRetentionException existing = new(
            new IOException("already-redacted-inner-secret"));
        FakeRepository repository = new()
        {
            ReadException = existing
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        Func<Task> action = () => service.PruneRequiredAsync(CancellationToken.None);

        TvGenerationRetentionException thrown = (await action.Should()
            .ThrowAsync<TvGenerationRetentionException>())
            .Which;
        thrown.Should().BeSameAs(existing);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task PruneRequiredAsync_OpaqueIdentity_LogsCountOnly()
    {
        const string opaqueIdentity = "malformed-secret-generation-token";
        FakeRepository repository = new()
        {
            Snapshot = new TvGenerationRetentionSnapshot(
                CurrentGenerationId,
                [new TvStoredGenerationSummary(CurrentGenerationId, Now.ToUniversalTime())],
                [opaqueIdentity]),
            DeleteResult = new TvGenerationRetentionDeleteResult(2, 3, 4)
        };
        ListLogger<TvGenerationRetentionService> logger = new();
        TvGenerationRetentionService service = CreateService(repository, logger);

        await service.PruneRequiredAsync(CancellationToken.None);

        repository.LastPlan!.UncertainOrphanGenerationIds.Should().Equal(opaqueIdentity);
        LogEntry information = logger.Entries.Should().ContainSingle().Subject;
        information.Message.Should().ContainAll(
            "UncertainOrphans=1",
            "ShowDocumentsDeleted=2",
            "LifecycleEventsDeleted=3",
            "ManifestsDeleted=4");
        information.Message.Should().NotContain(opaqueIdentity);
    }

    [Fact]
    public void ListLogger_PreservesPassedExceptionSeparately()
    {
        InvalidOperationException exception = new("synthetic-logger-secret");
        ListLogger<TvGenerationRetentionService> logger = new();

        logger.LogError(
            exception,
            "Synthetic logger event with exception type {ExceptionType}.",
            nameof(InvalidOperationException));

        LogEntry entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Exception.Should().BeSameAs(exception);
        entry.Message.Should().Contain(nameof(InvalidOperationException));
        entry.StructuredState.Should().Contain(
            pair => pair.Key == "ExceptionType"
                && Equals(pair.Value, nameof(InvalidOperationException)));
    }

    private static void AssertRedactedFailureLog(
        LogEntry entry,
        string mode,
        string code,
        string exceptionType,
        string secret)
    {
        entry.Exception.Should().BeNull();
        entry.Message.Should().ContainAll(mode, code, exceptionType);
        entry.Message.Should().NotContain(secret);
        entry.StructuredState.Should().Contain(
            pair => pair.Key == "Mode" && Equals(pair.Value, mode));
        entry.StructuredState.Should().Contain(
            pair => pair.Key == "Code" && Equals(pair.Value, code));
        entry.StructuredState.Should().Contain(
            pair => pair.Key == "ExceptionType" && Equals(pair.Value, exceptionType));
        entry.StructuredState
            .Select(pair => pair.Value?.ToString())
            .Should()
            .OnlyContain(value => value == null || !value.Contains(secret, StringComparison.Ordinal));
    }

    private static TvGenerationRetentionService CreateService(
        FakeRepository repository,
        ListLogger<TvGenerationRetentionService> logger)
    {
        return new TvGenerationRetentionService(
            repository,
            new TvGenerationRetentionPlanner(),
            Policy,
            new FixedTimeProvider(Now),
            logger);
    }

    private static TvGenerationRetentionSnapshot CurrentOnlySnapshot()
    {
        return new TvGenerationRetentionSnapshot(
            CurrentGenerationId,
            [new TvStoredGenerationSummary(CurrentGenerationId, Now.ToUniversalTime())],
            []);
    }

    private sealed class FakeRepository : ITvGenerationRetentionRepository
    {
        public TvGenerationRetentionSnapshot Snapshot { get; init; } = CurrentOnlySnapshot();

        public TvGenerationRetentionDeleteResult DeleteResult { get; init; } = new(0, 0, 0);

        public Exception? ReadException { get; init; }

        public Exception? ApplyException { get; init; }

        public int ReadCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public TvGenerationRetentionPlan? LastPlan { get; private set; }

        public Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            return ReadException is null
                ? Task.FromResult(Snapshot)
                : Task.FromException<TvGenerationRetentionSnapshot>(ReadException);
        }

        public Task<TvGenerationRetentionDeleteResult> ApplyAsync(
            TvGenerationRetentionPlan plan,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            LastPlan = plan;
            return ApplyException is null
                ? Task.FromResult(DeleteResult)
                : Task.FromException<TvGenerationRetentionDeleteResult>(ApplyException);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> StructuredState,
        Exception? Exception);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>> structuredState =
                state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToArray()
                    : [];
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                structuredState,
                exception));
        }
    }
}
