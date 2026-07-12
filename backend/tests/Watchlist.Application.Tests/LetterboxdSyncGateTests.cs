using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class LetterboxdSyncGateTests
{
    [Fact]
    public async Task RunAsync_WhenFirstOperationIsActive_WaitsBeforeStartingSecond()
    {
        LetterboxdSyncGate gate = new();
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> first = gate.RunAsync(async () =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return 1;
        }, CancellationToken.None);
        await firstStarted.Task;

        Task<int> second = gate.RunAsync(() =>
        {
            secondStarted.SetResult();
            return Task.FromResult(2);
        }, CancellationToken.None);

        secondStarted.Task.IsCompleted.Should().BeFalse();
        releaseFirst.SetResult();

        (await first).Should().Be(1);
        (await second).Should().Be(2);
        secondStarted.Task.IsCompleted.Should().BeTrue();
    }
}
