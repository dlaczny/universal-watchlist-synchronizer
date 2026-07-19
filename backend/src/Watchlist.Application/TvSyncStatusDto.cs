namespace Watchlist.Application;

public sealed record TvSyncStatusDto(
    string ConnectionStatus,
    DateTimeOffset? LastActivityPoll,
    DateTimeOffset? LastCompleteGeneration,
    TimeSpan? GenerationAge,
    int ActiveCount,
    int CaughtUpCount,
    int SourceRemovedCount,
    int ProviderErrorCount,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons);
