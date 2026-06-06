using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record WatchlistPrimaryAction(
    string Label,
    bool Enabled,
    string? Target);

public static class WatchlistPrimaryActionMapper
{
    public static WatchlistPrimaryAction FromAvailability(AvailabilityStatus status)
    {
        return status switch
        {
            AvailabilityStatus.AvailableOnPlex => new WatchlistPrimaryAction("Open in Plex", true, null),
            AvailabilityStatus.Unreleased => new WatchlistPrimaryAction("Not released", false, null),
            AvailabilityStatus.UnknownMatch => new WatchlistPrimaryAction("Match uncertain", false, null),
            _ => new WatchlistPrimaryAction("Unavailable", false, null)
        };
    }
}
