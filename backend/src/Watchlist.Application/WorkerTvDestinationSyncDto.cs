namespace Watchlist.Application;

public sealed record WorkerTvDestinationSyncDto(
    bool Capable,
    IReadOnlyList<string> Blockers);
