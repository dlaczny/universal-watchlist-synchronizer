using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Provides read-only watchlist queries for clients.
/// </summary>
public sealed class WatchlistQueryService(
    IWatchlistReadRepository repository,
    IPlexMovieInventoryRepository plexRepository,
    ITvShowReadRepository tvRepository)
{
    public WatchlistQueryService(IWatchlistReadRepository repository)
        : this(
            repository,
            NullPlexMovieInventoryRepository.Instance,
            NullTvShowReadRepository.Instance)
    {
    }

    public WatchlistQueryService(
        IWatchlistReadRepository repository,
        ITvShowReadRepository tvRepository)
        : this(repository, NullPlexMovieInventoryRepository.Instance, tvRepository)
    {
    }

    public WatchlistQueryService(
        IWatchlistReadRepository repository,
        IPlexMovieInventoryRepository plexRepository)
        : this(repository, plexRepository, NullTvShowReadRepository.Instance)
    {
    }

    /// <summary>
    /// Gets watchlist items filtered and sorted by backend-owned query controls.
    /// </summary>
    public async Task<IReadOnlyList<WatchlistItemDto>> GetItemsAsync(
        WatchlistQuery query,
        CancellationToken cancellationToken)
    {
        List<WatchlistItemDto> browseItems = [];

        if (query.Collection is WatchlistCollection.All or WatchlistCollection.Movie)
        {
            IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
            browseItems.AddRange(items
                .Where(item => item.MediaType == MediaType.Movie)
                .Where(item => query.Availability.Contains(item.AvailabilityStatus))
                .Select(ToDto));
        }

        if (query.Collection is (WatchlistCollection.All or WatchlistCollection.Tv)
            && query.Availability.Contains(AvailabilityStatus.UnknownMatch))
        {
            PublishedTvGeneration? generation = await tvRepository.GetPublishedAsync(cancellationToken);
            IEnumerable<TvShow> shows = generation?.Shows ?? [];
            TvBrowseState desiredState = query.Collection == WatchlistCollection.All
                ? TvBrowseState.Active
                : query.TvState ?? TvBrowseState.Active;
            browseItems.AddRange(shows
                .Where(show => MatchesBrowseState(show, desiredState))
                .Select(ToTvDto));
        }

        if (query.Availability.Contains(AvailabilityStatus.AvailableOnPlex))
        {
            IReadOnlyList<PlexMovieDto> unmatchedMovies =
                await plexRepository.GetUnmatchedMoviesAsync(cancellationToken);
            IEnumerable<PlexMovieDto> filteredPlexMovies = query.Collection == WatchlistCollection.Tv
                ? []
                : unmatchedMovies;

            browseItems.AddRange(filteredPlexMovies.Select(ToPlexOnlyDto));
        }

        IEnumerable<WatchlistItemDto> sortedItems = query.Sort switch
        {
            WatchlistSort.TitleAscending => browseItems.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            _ => browseItems.OrderByDescending(item => item.AddedAt)
        };

        return sortedItems.ToList();
    }

    /// <summary>
    /// Gets a single watchlist item by identifier.
    /// </summary>
    public async Task<WatchlistItemDto?> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        if (IsPublishedTvId(id))
        {
            TvShow? tvShow = await tvRepository.GetPublishedShowAsync(id, cancellationToken);
            return tvShow is null ? null : ToTvDto(tvShow);
        }

        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.FirstOrDefault(item => item.Id == id && item.MediaType == MediaType.Movie);

        if (item is not null)
        {
            return ToDto(item);
        }

        string? plexRatingKey = TryParsePlexMovieId(id);
        if (plexRatingKey is null)
        {
            return null;
        }

        PlexMovieDto? plexMovie = await plexRepository.GetMovieAsync(plexRatingKey, cancellationToken);
        return plexMovie is null ? null : ToPlexOnlyDto(plexMovie);
    }

    /// <summary>
    /// Gets detailed watchlist item information including detail fields and primary action.
    /// </summary>
    public async Task<WatchlistItemDetailsDto?> GetItemDetailsAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (IsPublishedTvId(id))
        {
            TvShow? tvShow = await tvRepository.GetPublishedShowAsync(id, cancellationToken);
            return tvShow is null ? null : ToTvDetailsDto(tvShow);
        }

        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.FirstOrDefault(item => item.Id == id && item.MediaType == MediaType.Movie);
        if (item is not null)
        {
            return ToDetailsDto(item);
        }

        string? plexRatingKey = TryParsePlexMovieId(id);
        if (plexRatingKey is null)
        {
            return null;
        }

        PlexMovieDto? plexMovie = await plexRepository.GetMovieAsync(plexRatingKey, cancellationToken);
        return plexMovie is null ? null : ToPlexOnlyDetailsDto(plexMovie);
    }

    private static bool MatchesBrowseState(TvShow show, TvBrowseState state)
    {
        return (show.LifecycleState, state) switch
        {
            (TvLifecycleState.Active, TvBrowseState.Active) => true,
            (TvLifecycleState.CaughtUp, TvBrowseState.CaughtUp) => true,
            (TvLifecycleState.RetiredTerminal, TvBrowseState.Retired) => true,
            _ => false
        };
    }

    private static WatchlistItemDto ToTvDto(TvShow show)
    {
        return new WatchlistItemDto(
            show.Id,
            "tv",
            "trakt",
            show.TraktId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            show.Title,
            show.Year,
            show.Overview,
            show.PosterUrl,
            show.BackdropUrl,
            ToReleaseStatus(show),
            "unknown_match",
            "watchlist",
            false,
            false,
            [],
            show.Availability.Offers
                .Where(offer => offer.Category == TvProviderCategory.Flatrate)
                .Select(offer => offer.ProviderName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            show.AddedAt,
            show.UpdatedAt)
        {
            Tv = ToTvBrowseDto(show)
        };
    }

    private static WatchlistItemDetailsDto ToTvDetailsDto(TvShow show)
    {
        return new WatchlistItemDetailsDto(
            show.Id,
            "tv",
            "trakt",
            show.TraktId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            show.Title,
            show.Year,
            show.Overview,
            show.PosterUrl,
            show.BackdropUrl,
            ToReleaseStatus(show),
            "unknown_match",
            "watchlist",
            false,
            false,
            [],
            show.Availability.Offers
                .Where(offer => offer.Category == TvProviderCategory.Flatrate)
                .Select(offer => offer.ProviderName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            show.AddedAt,
            show.UpdatedAt,
            [],
            null,
            null,
            null,
            null,
            "Unavailable",
            false,
            null)
        {
            Tv = ToTvDetailsDtoValue(show)
        };
    }

    private static TvBrowseDto ToTvBrowseDto(TvShow show)
    {
        TvSeasonProgress? relevantSeason = FindRelevantSeason(show);
        return new TvBrowseDto(
            1,
            ToApiValue(show.LifecycleState),
            show.LastLifecycleEvent,
            show.TraktStatus,
            show.InWatchlist,
            ToApiValue(show.IdentityStatus),
            show.AiredEpisodes,
            show.CompletedEpisodes,
            ToEpisodeDto(show.NextEpisode),
            false,
            "unknown",
            ToProviderAvailabilityDto(show.Availability),
            relevantSeason?.SeasonNumber,
            relevantSeason is null ? null : ToProviderAvailabilityDto(relevantSeason.Availability));
    }

    private static TvDetailsDto ToTvDetailsDtoValue(TvShow show)
    {
        IReadOnlyList<TvSeasonProgressDto> seasons = show.Seasons
            .OrderBy(season => season.SeasonNumber)
            .Select(season => new TvSeasonProgressDto(
                season.SeasonNumber,
                season.AiredEpisodes,
                season.CompletedEpisodes,
                season.HasKnownFutureEpisode,
                "not_implemented",
                ToProviderAvailabilityDto(season.Availability),
                season.Episodes
                    .OrderBy(episode => episode.EpisodeNumber)
                    .Select(episode => ToEpisodeDto(episode)!)
                    .ToArray()))
            .ToArray();

        return new TvDetailsDto(
            1,
            ToApiValue(show.LifecycleState),
            show.LastLifecycleEvent,
            show.TraktStatus,
            show.InWatchlist,
            ToApiValue(show.IdentityStatus),
            show.AiredEpisodes,
            show.CompletedEpisodes,
            ToEpisodeDto(show.LastWatchedEpisode),
            ToEpisodeDto(show.NextEpisode),
            ToProviderAvailabilityDto(show.Availability),
            new TvDestinationStatusDto("unknown", "unknown", null),
            seasons);
    }

    private static TvSeasonProgress? FindRelevantSeason(TvShow show)
    {
        if (show.NextEpisode is not null)
        {
            return show.Seasons.SingleOrDefault(
                season => season.SeasonNumber == show.NextEpisode.SeasonNumber);
        }

        return show.Seasons
            .Where(season => season.SeasonNumber > 0 && season.AiredEpisodes > 0)
            .OrderByDescending(season => season.SeasonNumber)
            .FirstOrDefault();
    }

    private static TvEpisodeProgressDto? ToEpisodeDto(TvEpisodeProgress? episode)
    {
        return episode is null
            ? null
            : new TvEpisodeProgressDto(
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.Title,
                episode.AiredAt,
                episode.Watched,
                episode.WatchedAt);
    }

    private static TvProviderAvailabilityDto ToProviderAvailabilityDto(TvProviderAvailability availability)
    {
        return new TvProviderAvailabilityDto(
            ToApiValue(availability.State),
            availability.Region,
            availability.FetchedAt,
            availability.Link,
            availability.Offers
                .OrderBy(offer => offer.Category)
                .ThenBy(offer => offer.ProviderId)
                .Select(offer => new TvProviderOfferDto(
                    offer.ProviderId,
                    offer.ProviderName,
                    ToApiValue(offer.Category),
                    offer.LogoUrl))
                .ToArray());
    }

    private static string ToReleaseStatus(TvShow show)
    {
        if (show.AiredEpisodes > 0)
        {
            return "released";
        }

        return show.NextEpisode?.AiredAt is DateTimeOffset airedAt
            && airedAt > DateTimeOffset.UtcNow
            ? "unreleased"
            : "unknown";
    }

    private static WatchlistItemDto ToDto(WatchlistItem item)
    {
        return new WatchlistItemDto(
            item.Id,
            ToApiValue(item.MediaType),
            ToApiValue(item.Source),
            item.SourceId,
            item.Title,
            item.Year,
            item.Overview,
            item.PosterUrl,
            item.BackdropUrl,
            ToApiValue(item.ReleaseStatus),
            ToApiValue(item.AvailabilityStatus),
            ToApiValue(MembershipFor(item)),
            item.VodReleaseKnown,
            item.ReleasedOnVod,
            item.VodRegions,
            item.OwnedServiceAvailability,
            item.AddedAt,
            item.UpdatedAt);
    }

    private static WatchlistItemDetailsDto ToDetailsDto(WatchlistItem item)
    {
        WatchlistPrimaryAction action = WatchlistPrimaryActionMapper.FromAvailability(item.AvailabilityStatus);

        return new WatchlistItemDetailsDto(
            item.Id,
            ToApiValue(item.MediaType),
            ToApiValue(item.Source),
            item.SourceId,
            item.Title,
            item.Year,
            item.Overview,
            item.PosterUrl,
            item.BackdropUrl,
            ToApiValue(item.ReleaseStatus),
            ToApiValue(item.AvailabilityStatus),
            ToApiValue(MembershipFor(item)),
            item.VodReleaseKnown,
            item.ReleasedOnVod,
            item.VodRegions,
            item.OwnedServiceAvailability,
            item.AddedAt,
            item.UpdatedAt,
            item.Genres,
            item.RuntimeMinutes,
            item.OriginalLanguage,
            item.TmdbVoteAverage,
            item.TmdbVoteCount,
            action.Label,
            action.Enabled,
            action.Target);
    }

    private static WatchlistItemDto ToPlexOnlyDto(PlexMovieDto movie)
    {
        DateTimeOffset timestamp = movie.LastSeenAt == default
            ? DateTimeOffset.UnixEpoch
            : movie.LastSeenAt;

        return new WatchlistItemDto(
            ToPlexMovieId(movie.RatingKey),
            "movie",
            "plex",
            movie.RatingKey,
            movie.Title,
            movie.Year,
            movie.Summary,
            ToPlexImageUrl(movie, "poster", movie.PosterPath),
            ToPlexImageUrl(movie, "backdrop", movie.BackdropPath),
            "unknown",
            "available_on_plex",
            "plex_only",
            false,
            false,
            [],
            ["plex"],
            timestamp,
            timestamp);
    }

    private static WatchlistItemDetailsDto ToPlexOnlyDetailsDto(PlexMovieDto movie)
    {
        DateTimeOffset timestamp = movie.LastSeenAt == default
            ? DateTimeOffset.UnixEpoch
            : movie.LastSeenAt;

        return new WatchlistItemDetailsDto(
            ToPlexMovieId(movie.RatingKey),
            "movie",
            "plex",
            movie.RatingKey,
            movie.Title,
            movie.Year,
            movie.Summary,
            ToPlexImageUrl(movie, "poster", movie.PosterPath),
            ToPlexImageUrl(movie, "backdrop", movie.BackdropPath),
            "unknown",
            "available_on_plex",
            "plex_only",
            false,
            false,
            [],
            ["plex"],
            timestamp,
            timestamp,
            [],
            null,
            null,
            null,
            null,
            "Unavailable",
            false,
            null);
    }

    private static string ToPlexMovieId(string ratingKey)
    {
        return $"plex-movie-{ratingKey}";
    }

    private static string? ToPlexImageUrl(PlexMovieDto movie, string kind, string? plexPath)
    {
        return string.IsNullOrWhiteSpace(plexPath)
            ? null
            : $"/api/images/plex/{Uri.EscapeDataString(movie.RatingKey)}/{kind}";
    }

    private static string? TryParsePlexMovieId(string id)
    {
        const string Prefix = "plex-movie-";
        return id.StartsWith(Prefix, StringComparison.Ordinal)
            ? id[Prefix.Length..]
            : null;
    }

    private static string ToApiValue(LibraryMembership membership)
    {
        return membership switch
        {
            LibraryMembership.Watchlist => "watchlist",
            LibraryMembership.WatchlistAndPlex => "watchlist_and_plex",
            LibraryMembership.PlexOnly => "plex_only",
            _ => "watchlist"
        };
    }

    private static LibraryMembership MembershipFor(WatchlistItem item)
    {
        return item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            ? LibraryMembership.WatchlistAndPlex
            : LibraryMembership.Watchlist;
    }

    private static string ToApiValue(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Movie => "movie",
            MediaType.TvShow => "tv",
            MediaType.Unspecified => "unspecified",
            _ => "unspecified"
        };
    }

    private static string ToApiValue(WatchlistSource source)
    {
        return source switch
        {
            WatchlistSource.Letterboxd => "letterboxd",
            WatchlistSource.Tmdb => "tmdb",
            WatchlistSource.Unspecified => "unspecified",
            _ => "unspecified"
        };
    }

    private static string ToApiValue(ReleaseStatus releaseStatus)
    {
        return releaseStatus switch
        {
            ReleaseStatus.Released => "released",
            ReleaseStatus.Unreleased => "unreleased",
            ReleaseStatus.Unknown => "unknown",
            _ => "unknown"
        };
    }

    private static string ToApiValue(AvailabilityStatus availabilityStatus)
    {
        return availabilityStatus switch
        {
            AvailabilityStatus.AvailableOnPlex => "available_on_plex",
            AvailabilityStatus.NotOnPlex => "not_on_plex",
            AvailabilityStatus.Unreleased => "unreleased",
            AvailabilityStatus.UnknownMatch => "unknown_match",
            AvailabilityStatus.Unspecified => "unspecified",
            _ => "unspecified"
        };
    }

    private static bool IsPublishedTvId(string id)
    {
        return id.StartsWith("tv-trakt-", StringComparison.Ordinal);
    }

    private static string ToApiValue(TvLifecycleState lifecycleState)
    {
        return lifecycleState switch
        {
            TvLifecycleState.Active => "active",
            TvLifecycleState.CaughtUp => "caught_up",
            TvLifecycleState.SourceRemoved => "source_removed",
            TvLifecycleState.TerminalCleanupPending => "terminal_cleanup_pending",
            TvLifecycleState.RetiredTerminal => "retired_terminal",
            _ => "unknown"
        };
    }

    private static string ToApiValue(TvIdentityStatus identityStatus)
    {
        return identityStatus switch
        {
            TvIdentityStatus.Verified => "verified",
            TvIdentityStatus.Missing => "missing",
            TvIdentityStatus.Conflict => "conflict",
            TvIdentityStatus.LegacyUnresolved => "legacy_unresolved",
            _ => "unknown"
        };
    }

    private static string ToApiValue(TvProviderState providerState)
    {
        return providerState switch
        {
            TvProviderState.Available => "available",
            TvProviderState.ConfirmedUnavailable => "confirmed_unavailable",
            TvProviderState.Unknown => "unknown",
            TvProviderState.Stale => "stale",
            _ => "unknown"
        };
    }

    private static string ToApiValue(TvProviderCategory category)
    {
        return category switch
        {
            TvProviderCategory.Flatrate => "flatrate",
            TvProviderCategory.Free => "free",
            TvProviderCategory.Ads => "ads",
            TvProviderCategory.Rent => "rent",
            TvProviderCategory.Buy => "buy",
            _ => "unknown"
        };
    }

    private enum LibraryMembership
    {
        Watchlist,
        WatchlistAndPlex,
        PlexOnly
    }

    private sealed class NullPlexMovieInventoryRepository : IPlexMovieInventoryRepository
    {
        public static readonly NullPlexMovieInventoryRepository Instance = new();

        private NullPlexMovieInventoryRepository()
        {
        }

        public Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
            IReadOnlyList<PlexMovieDto> movies,
            IReadOnlySet<string> scannedSectionKeys,
            DateTimeOffset syncTime,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlexInventoryApplyResult(0, 0));
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlexMovieDto>>([]);
        }

        public Task<IReadOnlyList<PlexMovieDto>> GetUnmatchedMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PlexMovieDto>>([]);
        }

        public Task<PlexMovieDto?> GetMovieAsync(string ratingKey, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlexMovieDto?>(null);
        }

        public Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItemWriteModel>>([]);
        }

        public Task ApplyMatchUpdatesAsync(
            IReadOnlyList<PlexMovieMatchUpdate> updates,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NullTvShowReadRepository : ITvShowReadRepository
    {
        public static readonly NullTvShowReadRepository Instance = new();

        private NullTvShowReadRepository()
        {
        }

        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<PublishedTvGeneration?>(null);
        }

        public Task<TvShow?> GetPublishedShowAsync(string id, CancellationToken cancellationToken)
        {
            return Task.FromResult<TvShow?>(null);
        }
    }
}
