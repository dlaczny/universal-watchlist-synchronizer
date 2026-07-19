namespace Watchlist.Application;

public static class TvBlockerCodes
{
    public const string Phase1ReadOnly = "phase_1_read_only";
    public const string IdentityMissingTvdb = "identity_missing_tvdb";
    public const string IdentityConflict = "identity_conflict";
    public const string TraktOutboxUnresolved = "trakt_outbox_unresolved";
    public const string PlexEventQuarantined = "plex_event_quarantined";
    public const string PlexHistoryUnavailable = "plex_history_unavailable";
    public const string PlexBackfillIncomplete = "plex_backfill_incomplete";
    public const string PlexEvidenceStale = "plex_evidence_stale";
    public const string ExplicitTraktWatchlist = "explicit_trakt_watchlist";
    public const string TraktNextEpisodeKnown = "trakt_next_episode_known";
}
