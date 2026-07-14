namespace Watchlist.Domain;

public sealed record TvShow(
    string Id,
    long TraktId,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    TvIdentityStatus IdentityStatus,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    string TraktStatus,
    bool InWatchlist,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgress? LastWatchedEpisode,
    TvEpisodeProgress? NextEpisode,
    IReadOnlyList<TvSeasonProgress> Seasons,
    IReadOnlyList<TvSpecialEpisodeIdentity> SpecialEpisodeIdentities,
    TvProviderAvailability Availability,
    TvLifecycleState LifecycleState,
    string? LastLifecycleEvent,
    long LifecycleVersion,
    int MissingScheduledConfirmations,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset MetadataFetchedAt,
    string GenerationId,
    string? LegacySourceId)
{
    private IReadOnlyList<TvSeasonProgress> _seasons = Snapshot(Seasons);
    private IReadOnlyList<TvSpecialEpisodeIdentity> _specialEpisodeIdentities =
        Snapshot(SpecialEpisodeIdentities);

    public IReadOnlyList<TvSeasonProgress> Seasons
    {
        get => _seasons;
        init => _seasons = Snapshot(value);
    }

    public IReadOnlyList<TvSpecialEpisodeIdentity> SpecialEpisodeIdentities
    {
        get => _specialEpisodeIdentities;
        init => _specialEpisodeIdentities = Snapshot(value);
    }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
