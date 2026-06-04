using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class TmdbMovieEnrichmentService(
    ITmdbMovieClient client,
    ITmdbMovieMetadataRepository repository,
    TimeProvider timeProvider) : ITmdbMovieEnrichmentService
{
    private const string CompletedStatus = "completed";
    private const string EnrichedStatus = "enriched";
    private const string FailedStatus = "failed";
    private const string NotFoundStatus = "not_found";
    private const string PartialStatus = "partial";

    private static readonly HashSet<string> OwnedProviderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "max",
        "hbo max",
        "skyshowtime",
        "crunchyroll",
        "amazon prime video",
        "prime video"
    };

    public async Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<WatchlistItemWriteModel> items = await repository.GetLetterboxdMoviesAsync(cancellationToken);
        List<WatchlistItemWriteModel> letterboxdMovies = items
            .Where(IsLetterboxdMovie)
            .ToList();

        int enriched = 0;
        int notFound = 0;
        int failed = 0;

        foreach (WatchlistItemWriteModel item in letterboxdMovies)
        {
            MovieEnrichmentOutcome outcome = await EnrichForBatchAsync(item, cancellationToken);
            if (outcome == MovieEnrichmentOutcome.Enriched)
            {
                enriched++;
            }
            else if (outcome == MovieEnrichmentOutcome.NotFound)
            {
                notFound++;
            }
            else
            {
                failed++;
            }
        }

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();
        string status = failed == 0 ? CompletedStatus : PartialStatus;

        return new TmdbMovieEnrichmentResultDto(
            status,
            startedAt,
            finishedAt,
            letterboxdMovies.Count,
            enriched,
            notFound,
            failed);
    }

    public async Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(
        string id,
        CancellationToken cancellationToken)
    {
        WatchlistItemWriteModel? item = await repository.GetLetterboxdMovieAsync(id, cancellationToken);
        if (item is null || !IsLetterboxdMovie(item))
        {
            return null;
        }

        if (!TryParseCandidateTmdbId(item, out int candidateTmdbId, out string? parseError))
        {
            await ApplyFailureAsync(item.Item.Id, FailedStatus, parseError, cancellationToken);
            return new TmdbSingleMovieEnrichmentResultDto(FailedStatus, item.Item.Id, null);
        }

        TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
            candidateTmdbId,
            item.ImdbId,
            cancellationToken);
        TmdbMovieMetadataUpdate update = CreateEnrichedUpdate(metadata);

        await repository.ApplyTmdbMetadataAsync(item.Item.Id, update, cancellationToken);

        return new TmdbSingleMovieEnrichmentResultDto(EnrichedStatus, item.Item.Id, metadata.Details.TmdbId);
    }

    private async Task<MovieEnrichmentOutcome> EnrichForBatchAsync(
        WatchlistItemWriteModel item,
        CancellationToken cancellationToken)
    {
        if (!TryParseCandidateTmdbId(item, out int candidateTmdbId, out string? parseError))
        {
            await ApplyFailureAsync(item.Item.Id, FailedStatus, parseError, cancellationToken);
            return MovieEnrichmentOutcome.Failed;
        }

        try
        {
            TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
                candidateTmdbId,
                item.ImdbId,
                cancellationToken);
            TmdbMovieMetadataUpdate update = CreateEnrichedUpdate(metadata);

            await repository.ApplyTmdbMetadataAsync(item.Item.Id, update, cancellationToken);
            return MovieEnrichmentOutcome.Enriched;
        }
        catch (TmdbMovieNotFoundException exception)
        {
            await ApplyFailureAsync(item.Item.Id, NotFoundStatus, exception.Message, cancellationToken);
            return MovieEnrichmentOutcome.NotFound;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ApplyFailureAsync(item.Item.Id, FailedStatus, exception.Message, cancellationToken);
            return MovieEnrichmentOutcome.Failed;
        }
    }

    private TmdbMovieMetadataUpdate CreateEnrichedUpdate(TmdbMovieMetadataDto metadata)
    {
        TmdbMovieDetailsDto details = metadata.Details;
        IReadOnlyList<string> ownedServiceAvailability = GetOwnedServiceAvailability(metadata.Providers);
        IReadOnlyList<string> vodRegions = GetVodRegions(metadata.Providers);

        return new TmdbMovieMetadataUpdate(
            details.TmdbId,
            details.ImdbId,
            details.Title,
            details.OriginalTitle,
            details.Overview,
            details.ReleaseDate,
            details.Genres,
            details.PosterPath,
            details.BackdropPath,
            details.PosterUrl,
            details.BackdropUrl,
            metadata.Providers,
            ownedServiceAvailability,
            vodRegions.Count > 0,
            vodRegions,
            timeProvider.GetUtcNow(),
            EnrichedStatus,
            null);
    }

    private async Task ApplyFailureAsync(
        string id,
        string metadataStatus,
        string? metadataError,
        CancellationToken cancellationToken)
    {
        TmdbMovieMetadataUpdate update = new(
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            new TmdbMovieProviderDataDto(new Dictionary<string, TmdbRegionWatchProvidersDto>()),
            [],
            false,
            [],
            timeProvider.GetUtcNow(),
            metadataStatus,
            metadataError);

        await repository.ApplyTmdbMetadataAsync(id, update, cancellationToken);
    }

    private static bool TryParseCandidateTmdbId(
        WatchlistItemWriteModel item,
        out int candidateTmdbId,
        out string? error)
    {
        if (int.TryParse(item.Item.SourceId, out candidateTmdbId))
        {
            error = null;
            return true;
        }

        error = $"Letterboxd movie source id '{item.Item.SourceId}' is not a valid TMDB id.";
        return false;
    }

    private static IReadOnlyList<string> GetOwnedServiceAvailability(TmdbMovieProviderDataDto providers)
    {
        if (!providers.Regions.TryGetValue("PL", out TmdbRegionWatchProvidersDto? polandProviders))
        {
            return [];
        }

        return polandProviders.Flatrate
            .Where(provider => OwnedProviderNames.Contains(provider.ProviderName))
            .Select(provider => provider.ProviderName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetVodRegions(TmdbMovieProviderDataDto providers)
    {
        List<string> regions = [];
        AddVodRegionIfAvailable(providers, regions, "PL");
        AddVodRegionIfAvailable(providers, regions, "US");

        return regions;
    }

    private static void AddVodRegionIfAvailable(
        TmdbMovieProviderDataDto providers,
        List<string> regions,
        string region)
    {
        if (!providers.Regions.TryGetValue(region, out TmdbRegionWatchProvidersDto? regionProviders))
        {
            return;
        }

        if (regionProviders.Flatrate.Count > 0
            || regionProviders.Rent.Count > 0
            || regionProviders.Buy.Count > 0)
        {
            regions.Add(region);
        }
    }

    private static bool IsLetterboxdMovie(WatchlistItemWriteModel item)
    {
        return item.Item.MediaType == MediaType.Movie && item.Item.Source == WatchlistSource.Letterboxd;
    }

    private enum MovieEnrichmentOutcome
    {
        Enriched,
        NotFound,
        Failed
    }
}
