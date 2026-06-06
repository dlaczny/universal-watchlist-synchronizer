using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class TmdbTvWatchlistSyncService(
    ITmdbTvWatchlistClient watchlistClient,
    ITmdbTvMetadataClient metadataClient,
    IWatchlistWriteRepository repository,
    TimeProvider timeProvider) : ITmdbTvWatchlistSyncService
{
    public async Task<TmdbTvSyncResultDto> SyncAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        IReadOnlyList<TmdbTvWatchlistItemDto> sourceItems = await watchlistClient.GetWatchlistAsync(cancellationToken);

        HashSet<string> sourceIds = new(sourceItems.Select(item => item.TmdbId.ToString()), StringComparer.Ordinal);

        List<WatchlistItemWriteModel> writeModels = [];
        int itemsEnriched = 0;
        int itemsNotFound = 0;
        int itemsFailed = 0;

        foreach (TmdbTvWatchlistItemDto sourceItem in sourceItems)
        {
            TmdbTvMetadataDto metadata;
            try
            {
                metadata = await metadataClient.GetTvMetadataAsync(sourceItem.TmdbId, cancellationToken);
            }
            catch (TmdbTvNotFoundException)
            {
                itemsNotFound++;
                continue;
            }
            catch (Exception)
            {
                itemsFailed++;
                continue;
            }

            itemsEnriched++;
            WatchlistItemWriteModel writeModel = ToWriteModel(metadata, startedAt);
            writeModels.Add(writeModel);
        }

        TmdbTvWatchlistApplyResult applyResult = await repository.ApplyTmdbTvWatchlistSyncAsync(
            writeModels,
            sourceIds,
            itemsFailed > 0 ? "tmdb_tv_partial" : "tmdb_tv_completed",
            startedAt,
            cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new TmdbTvSyncResultDto(
            itemsFailed > 0 ? "partial" : "completed",
            startedAt,
            finishedAt,
            sourceItems.Count,
            applyResult.ItemsUpserted,
            applyResult.ItemsDeleted,
            itemsEnriched,
            itemsNotFound,
            itemsFailed);
    }

    private static WatchlistItemWriteModel ToWriteModel(
        TmdbTvMetadataDto metadata,
        DateTimeOffset syncTime)
    {
        (ReleaseStatus releaseStatus, AvailabilityStatus availabilityStatus) =
            ToReleaseAndAvailabilityStatus(metadata.FirstAirDate, metadata.Status, syncTime);

        int? year = null;
        if (DateTimeOffset.TryParse(metadata.FirstAirDate, out DateTimeOffset parsedDate))
        {
            year = parsedDate.Year;
        }

        WatchlistItem item = new(
            $"tv-tmdb-{metadata.TmdbId}",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            metadata.TmdbId.ToString(),
            metadata.Name,
            year,
            metadata.Overview,
            metadata.PosterUrl,
            metadata.BackdropUrl,
            releaseStatus,
            availabilityStatus,
            syncTime,
            syncTime)
        {
            Genres = metadata.Genres,
            OriginalLanguage = metadata.OriginalLanguage,
            TmdbVoteAverage = metadata.TmdbVoteAverage,
            TmdbVoteCount = metadata.TmdbVoteCount
        };

        return new WatchlistItemWriteModel(
            item,
            metadata.ExternalIds.ImdbId,
            null,
            metadata.TmdbId);
    }

    private static (ReleaseStatus Release, AvailabilityStatus Availability) ToReleaseAndAvailabilityStatus(
        string? firstAirDate,
        string? status,
        DateTimeOffset syncTime)
    {
        ReleaseStatus release = ToReleaseStatus(firstAirDate, status, syncTime);

        AvailabilityStatus availability = release switch
        {
            ReleaseStatus.Unreleased => AvailabilityStatus.Unreleased,
            ReleaseStatus.Unknown => AvailabilityStatus.UnknownMatch,
            _ => AvailabilityStatus.NotOnPlex
        };

        return (release, availability);
    }

    private static ReleaseStatus ToReleaseStatus(string? firstAirDate, string? status, DateTimeOffset syncTime)
    {
        if (DateTimeOffset.TryParse(firstAirDate, out DateTimeOffset parsed)
            && parsed.Date > syncTime.Date)
        {
            return ReleaseStatus.Unreleased;
        }

        return status switch
        {
            "Ended" or "Returning Series" or "Canceled" or "In Production" => ReleaseStatus.Released,
            _ when !string.IsNullOrWhiteSpace(firstAirDate) => ReleaseStatus.Released,
            _ => ReleaseStatus.Unknown
        };
    }
}
