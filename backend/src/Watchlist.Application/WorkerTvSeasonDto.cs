namespace Watchlist.Application;

public sealed record WorkerTvSeasonDto(
    int SeasonNumber,
    int Aired,
    int Completed,
    bool MonitoredDesired,
    IReadOnlyList<int> SearchAiredUnwatchedEpisodes,
    string CleanupState,
    IReadOnlyList<WorkerTvEpisodeDto> Episodes);
