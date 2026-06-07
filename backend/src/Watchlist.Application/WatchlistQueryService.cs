using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Provides read-only watchlist queries for clients.
/// </summary>
public sealed class WatchlistQueryService(
    IWatchlistReadRepository repository,
    IPlexMovieInventoryRepository plexRepository)
{
    public WatchlistQueryService(IWatchlistReadRepository repository)
        : this(repository, NullPlexMovieInventoryRepository.Instance)
    {
    }

    /// <summary>
    /// Gets watchlist items filtered and sorted by backend-owned query controls.
    /// </summary>
    public async Task<IReadOnlyList<WatchlistItemDto>> GetItemsAsync(
        WatchlistQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        IEnumerable<WatchlistItem> filteredItems = items;

        MediaType? mediaType = query.Collection switch
        {
            WatchlistCollection.Movie => MediaType.Movie,
            WatchlistCollection.Tv => MediaType.TvShow,
            _ => null
        };

        if (mediaType is not null)
        {
            filteredItems = filteredItems.Where(item => item.MediaType == mediaType.Value);
        }

        filteredItems = filteredItems.Where(item => query.Availability.Contains(item.AvailabilityStatus));

        List<WatchlistItemDto> browseItems = filteredItems
            .Select(ToDto)
            .ToList();

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
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.FirstOrDefault(item => item.Id == id);

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
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.FirstOrDefault(item => item.Id == id);
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
}
