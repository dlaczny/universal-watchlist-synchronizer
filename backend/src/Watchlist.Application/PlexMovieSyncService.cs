using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class PlexMovieSyncService(
    IPlexLibraryClient client,
    IPlexMovieInventoryRepository repository,
    TimeProvider timeProvider) : IPlexMovieSyncService
{
    private const string CompletedResultStatus = "completed";

    public async Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<PlexLibrarySectionDto> sections = await client.GetSectionsAsync(cancellationToken);
        List<PlexLibrarySectionDto> movieSections = sections
            .Where(section => string.Equals(section.Type, "movie", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<PlexMovieDto> sourceMovies = [];
        foreach (PlexLibrarySectionDto section in movieSections)
        {
            sourceMovies.AddRange(await client.GetMoviesAsync(section, cancellationToken));
        }

        DateTimeOffset syncTime = timeProvider.GetUtcNow();
        HashSet<string> scannedSectionKeys = movieSections
            .Select(section => section.Key)
            .ToHashSet(StringComparer.Ordinal);

        PlexInventoryApplyResult inventoryResult = await repository.ApplyMovieInventoryAsync(
            sourceMovies,
            scannedSectionKeys,
            syncTime,
            cancellationToken);

        IReadOnlyList<PlexMovieDto> plexMovies = await repository.GetMoviesAsync(cancellationToken);
        IReadOnlyList<WatchlistItemWriteModel> watchlistMovies = await repository.GetWatchlistMoviesAsync(cancellationToken);

        List<PlexMovieMatchUpdate> updates = watchlistMovies
            .Select(movie => ToUpdate(movie, PlexMovieMatcher.Match(movie, plexMovies), syncTime))
            .ToList();

        await repository.ApplyMatchUpdatesAsync(
            updates,
            SyncRunStatuses.PlexMoviesCompleted,
            syncTime,
            cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new PlexMovieSyncResultDto(
            CompletedResultStatus,
            startedAt,
            finishedAt,
            movieSections.Count,
            sourceMovies.Count,
            inventoryResult.ItemsUpserted,
            inventoryResult.ItemsDeleted,
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex),
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.NotOnPlex
                || update.AvailabilityStatus == AvailabilityStatus.Unreleased),
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.UnknownMatch));
    }

    private static PlexMovieMatchUpdate ToUpdate(
        WatchlistItemWriteModel item,
        PlexMatchResult match,
        DateTimeOffset syncTime)
    {
        DateTimeOffset? matchedAt = match.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            ? syncTime
            : null;

        return new PlexMovieMatchUpdate(
            item.Item.Id,
            match.AvailabilityStatus,
            match.PlexRatingKey,
            matchedAt,
            match.MatchReason,
            match.MatchConfidence);
    }
}
