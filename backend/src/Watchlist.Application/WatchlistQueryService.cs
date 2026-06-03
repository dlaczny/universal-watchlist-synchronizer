using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Provides read-only watchlist queries for clients.
/// </summary>
public sealed class WatchlistQueryService(IWatchlistReadRepository repository)
{
    /// <summary>
    /// Gets watchlist items filtered by media type and availability.
    /// </summary>
    public async Task<IReadOnlyList<WatchlistItemDto>> GetItemsAsync(
        MediaType? mediaType,
        WatchlistFilter filter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        IEnumerable<WatchlistItem> filteredItems = items;

        if (mediaType is not null)
        {
            filteredItems = filteredItems.Where(item => item.MediaType == mediaType.Value);
        }

        if (filter == WatchlistFilter.Available)
        {
            filteredItems = filteredItems.Where(item => item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex);
        }

        return filteredItems.Select(ToDto).ToList();
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
            item.AddedAt,
            item.UpdatedAt);
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
