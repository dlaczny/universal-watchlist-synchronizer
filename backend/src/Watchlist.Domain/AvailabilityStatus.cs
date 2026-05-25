namespace Watchlist.Domain;

public enum AvailabilityStatus
{
    Unspecified = 0,
    AvailableOnPlex = 1,
    NotOnPlex = 2,
    Unreleased = 3,
    UnknownMatch = 4
}
