namespace Watchlist.Application;

public sealed record WorkerTvPlexHistoryDto(
    bool Capable,
    bool BootstrapComplete,
    string? MachineIdentifier,
    long? AccountId,
    string? LibrarySectionId,
    string? LibrarySectionTitle,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? Watermark);
