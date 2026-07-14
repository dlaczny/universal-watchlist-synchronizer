namespace Watchlist.Domain;

public enum TvProviderState
{
    Available = 0,
    ConfirmedUnavailable = 1,
    Unknown = 2,
    Stale = 3
}
