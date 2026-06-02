namespace Watchlist.Application;

public sealed record SyncStatusDto(
    string Status,
    DateTimeOffset? LastSuccessfulSyncAt);
