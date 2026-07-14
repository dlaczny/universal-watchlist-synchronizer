using FluentAssertions;
using Microsoft.Extensions.Logging;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TraktDeviceAuthorizationHostedServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");

    [Fact]
    public async Task ExecuteAsync_WhenPendingPollIsDue_CallsConnectionService()
    {
        HostedRepository repository = new(PendingConnection(Now, Now.AddMinutes(10)));
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now);
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        await hostedService.StopAsync(CancellationToken.None);

        connectionService.PollCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNextPollIsInFuture_DoesNotPollEarly()
    {
        HostedRepository repository = new(PendingConnection(
            Now.AddSeconds(1),
            Now.AddMinutes(10)));
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now);
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await repository.FirstRead.WaitAsync(TimeSpan.FromSeconds(1));
        ManualTimer timer = await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));

        connectionService.PollCallCount.Should().Be(0);
        timer.DueTime.Should().Be(TimeSpan.FromSeconds(1));
        timer.Period.Should().Be(Timeout.InfiniteTimeSpan);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_AfterOneSecondTimeProviderCadence_PollsWhenTimestampBecomesDue()
    {
        HostedRepository repository = new(PendingConnection(
            Now.AddSeconds(1),
            Now.AddMinutes(10)));
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now);
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        ManualTimer timer = await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        timer.Fire();

        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        connectionService.PollCallCount.Should().Be(1);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPendingCodeExpired_CallsServiceForLocalTerminalTransition()
    {
        HostedRepository repository = new(PendingConnection(
            Now.AddMinutes(1),
            Now));
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now);
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            timeProvider);

        await hostedService.StartAsync(CancellationToken.None);

        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        connectionService.PollCallCount.Should().Be(1);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsPersistedNextTimestampAfterServiceRestart()
    {
        HostedRepository repository = new(PendingConnection(
            Now.AddSeconds(12),
            Now.AddMinutes(10),
            TimeSpan.FromSeconds(12)));
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now.AddSeconds(11));
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        ManualTimer timer = await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        connectionService.PollCallCount.Should().Be(0);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        timer.Fire();

        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        connectionService.PollCallCount.Should().Be(1);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationalErrorOccurs_LogsStableCodeAndKeepsPolling()
    {
        string protectedSentinel = "protected-device-sentinel-5c137d";
        HostedRepository repository = new(PendingConnection(Now, Now.AddMinutes(10)) with
        {
            ProtectedDeviceCode = protectedSentinel,
            UserCode = "user-code-sentinel-82e957"
        });
        HostedConnectionService connectionService = new()
        {
            FailuresBeforeSuccess = 1
        };
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TraktDeviceAuthorizationHostedService hostedService = new(
            repository,
            connectionService,
            timeProvider,
            logger);

        await hostedService.StartAsync(CancellationToken.None);
        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        ManualTimer timer = await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        timer.Fire();

        await connectionService.WaitForPollCountAsync(2).WaitAsync(TimeSpan.FromSeconds(1));
        connectionService.PollCallCount.Should().Be(2);
        logger.Messages.Should().ContainSingle(message => message.Contains(
            "trakt_unavailable",
            StringComparison.Ordinal));
        string logs = string.Join(Environment.NewLine, logger.Messages);
        logs.Should().NotContain(protectedSentinel);
        logs.Should().NotContain("user-code-sentinel-82e957");
        logs.Should().NotContain("TraktConnection {");
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryReadHasOperationalError_KeepsRunning()
    {
        HostedRepository repository = new(PendingConnection(Now, Now.AddMinutes(10)))
        {
            ReadFailuresBeforeSuccess = 1
        };
        HostedConnectionService connectionService = new();
        ManualTimeProvider timeProvider = new(Now);
        CapturingLogger logger = new();
        TraktDeviceAuthorizationHostedService hostedService = new(
            repository,
            connectionService,
            timeProvider,
            logger);

        await hostedService.StartAsync(CancellationToken.None);
        await repository.FirstRead.WaitAsync(TimeSpan.FromSeconds(1));
        ManualTimer timer = await timeProvider.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        timer.Fire();

        await connectionService.WaitForPollCountAsync(1).WaitAsync(TimeSpan.FromSeconds(1));
        repository.ReadCallCount.Should().BeGreaterThanOrEqualTo(2);
        logger.Messages.Should().ContainSingle(message => message.Contains(
            "trakt_unavailable",
            StringComparison.Ordinal));
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhileWaitingForCadence_StopsPromptly()
    {
        HostedRepository repository = new(null);
        HostedConnectionService connectionService = new();
        TraktDeviceAuthorizationHostedService hostedService = CreateService(
            repository,
            connectionService,
            TimeProvider.System);
        await hostedService.StartAsync(CancellationToken.None);
        await repository.FirstRead.WaitAsync(TimeSpan.FromSeconds(1));

        Task stop = hostedService.StopAsync(CancellationToken.None);

        await stop.WaitAsync(TimeSpan.FromMilliseconds(500));
        connectionService.PollCallCount.Should().Be(0);
    }

    private static TraktDeviceAuthorizationHostedService CreateService(
        ITraktConnectionRepository repository,
        ITraktConnectionService connectionService,
        TimeProvider timeProvider)
    {
        return new TraktDeviceAuthorizationHostedService(
            repository,
            connectionService,
            timeProvider,
            new CapturingLogger());
    }

    private static TraktConnection PendingConnection(
        DateTimeOffset nextPollAt,
        DateTimeOffset expiresAt,
        TimeSpan? interval = null)
    {
        return new TraktConnection(
            "pending",
            "protected:device-code",
            null,
            "https://trakt.tv/activate",
            expiresAt,
            interval ?? TimeSpan.FromSeconds(5),
            nextPollAt,
            null,
            null,
            null,
            Now);
    }

    private sealed class HostedRepository(TraktConnection? connection)
        : ITraktConnectionRepository
    {
        private readonly TaskCompletionSource firstRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstRead => firstRead.Task;

        public int ReadFailuresBeforeSuccess { get; init; }

        public int ReadCallCount { get; private set; }

        public Task<TraktConnection?> GetAsync(CancellationToken cancellationToken)
        {
            ReadCallCount++;
            firstRead.TrySetResult();
            if (ReadCallCount <= ReadFailuresBeforeSuccess)
            {
                return Task.FromException<TraktConnection?>(new TraktUnavailableException());
            }

            return Task.FromResult(connection);
        }

        public Task SaveAsync(TraktConnection savedConnection, CancellationToken cancellationToken)
        {
            connection = savedConnection;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            connection = null;
            return Task.CompletedTask;
        }
    }

    private sealed class HostedConnectionService : ITraktConnectionService
    {
        private readonly object sync = new();
        private readonly Dictionary<int, TaskCompletionSource> pollWaiters = [];

        public int PollCallCount { get; private set; }

        public int FailuresBeforeSuccess { get; init; }

        public Task<TraktDeviceStartDto> StartDeviceAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TraktConnectionStatusDto> PollPendingAsync(CancellationToken cancellationToken)
        {
            int callCount;
            lock (sync)
            {
                PollCallCount++;
                callCount = PollCallCount;
                foreach (KeyValuePair<int, TaskCompletionSource> waiter in pollWaiters
                    .Where(waiter => waiter.Key <= callCount)
                    .ToList())
                {
                    waiter.Value.TrySetResult();
                    pollWaiters.Remove(waiter.Key);
                }
            }

            if (callCount <= FailuresBeforeSuccess)
            {
                return Task.FromException<TraktConnectionStatusDto>(
                    new TraktUnavailableException());
            }

            return Task.FromResult(new TraktConnectionStatusDto("pending", null, null, null));
        }

        public Task<TraktConnectionStatusDto> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new TraktConnectionStatusDto("pending", null, null, null));
        }

        public Task<TraktConnectionStatusDto> DisconnectAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task WaitForPollCountAsync(int count)
        {
            lock (sync)
            {
                if (PollCallCount >= count)
                {
                    return Task.CompletedTask;
                }

                TaskCompletionSource waiter = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                pollWaiters[count] = waiter;
                return waiter.Task;
            }
        }
    }

    private sealed class CapturingLogger : ILogger<TraktDeviceAuthorizationHostedService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object sync = new();
        private readonly Queue<ManualTimer> timers = [];
        private TaskCompletionSource<ManualTimer>? timerWaiter;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ManualTimer timer = new(callback, state, dueTime, period);
            TaskCompletionSource<ManualTimer>? waiter;
            lock (sync)
            {
                waiter = timerWaiter;
                timerWaiter = null;
                if (waiter is null)
                {
                    timers.Enqueue(timer);
                }
            }

            waiter?.TrySetResult(timer);
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

                timerWaiter ??= NewTimerSource();
                return timerWaiter.Task;
            }
        }

        public void Advance(TimeSpan amount)
        {
            utcNow = utcNow.Add(amount);
        }

        private static TaskCompletionSource<ManualTimer> NewTimerSource()
        {
            return new TaskCompletionSource<ManualTimer>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class ManualTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private bool disposed;

        public TimeSpan DueTime { get; private set; } = dueTime;

        public TimeSpan Period { get; private set; } = period;

        public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
        {
            if (disposed)
            {
                return false;
            }

            DueTime = newDueTime;
            Period = newPeriod;
            return true;
        }

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
