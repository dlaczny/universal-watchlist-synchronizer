using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Provides read-only watchlist queries for clients.
/// </summary>
public sealed class WatchlistQueryService(IWatchlistReadRepository repository)
{
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

        IEnumerable<WatchlistItem> sortedItems = query.Sort switch
        {
            WatchlistSort.TitleAscending => filteredItems.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            _ => filteredItems.OrderByDescending(item => item.AddedAt)
        };

        return sortedItems.Select(ToDto).ToList();
    }

    /// <summary>
    /// Gets a single watchlist item by identifier.
    /// </summary>
    public async Task<WatchlistItemDto?> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.FirstOrDefault(item => item.Id == id);

        return item is null ? null : ToDto(item);
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
        return item is null ? null : ToDetailsDto(item);
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
}
