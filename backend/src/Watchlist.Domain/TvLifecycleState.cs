namespace Watchlist.Domain;

public enum TvLifecycleState
{
    Active = 0,
    CaughtUp = 1,
    SourceRemoved = 2,
    TerminalCleanupPending = 3,
    RetiredTerminal = 4
}
