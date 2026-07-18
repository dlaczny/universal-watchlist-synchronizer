using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class TmdbTvEnrichmentService(
    ITmdbTvMetadataClient metadataClient,
    TmdbEnrichmentSettings settings) : ITmdbTvEnrichmentService
{
    public async Task<TmdbTvEnrichmentResult> EnrichAsync(
        TraktShowMetadata current,
        IReadOnlyList<int> numberedSeasonNumbers,
        TvShow? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateInputs(current, numberedSeasonNumbers, previous, now);
        int[] orderedSeasonNumbers = numberedSeasonNumbers.Order().ToArray();
        List<string> errors = [];

        MetadataState metadata = await ResolveMetadataAsync(
            current,
            previous,
            now,
            errors,
            cancellationToken);
        TvShow? providerPrevious = metadata.TmdbId is > 0
            && previous?.TmdbId == metadata.TmdbId
            ? previous
            : null;
        TvProviderAvailability showAvailability = await ResolveShowAvailabilityAsync(
            current.Ids.TraktId,
            metadata.TmdbId,
            providerPrevious?.Availability,
            now,
            errors,
            cancellationToken);
        IReadOnlyDictionary<int, TvProviderAvailability> seasonAvailability =
            await ResolveSeasonAvailabilityAsync(
                current.Ids.TraktId,
                metadata.TmdbId,
                orderedSeasonNumbers,
                providerPrevious,
                now,
                errors,
                cancellationToken);

        return new TmdbTvEnrichmentResult(
            metadata.TvdbId,
            metadata.TmdbId,
            metadata.ImdbId,
            metadata.IdentityStatus,
            metadata.Title,
            metadata.Year,
            metadata.Overview,
            metadata.PosterUrl,
            metadata.BackdropUrl,
            metadata.FetchedAt,
            showAvailability,
            seasonAvailability,
            errors);
    }

    private async Task<MetadataState> ResolveMetadataAsync(
        TraktShowMetadata current,
        TvShow? previous,
        DateTimeOffset now,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        int? sourceTmdbId = current.Ids.TmdbId;
        bool canReusePrevious = previous is not null
            && previous.TmdbId == sourceTmdbId
            && IsTvdbSourceCompatible(current.Ids.TvdbId, previous)
            && previous.MetadataFetchedAt <= now
            && now - previous.MetadataFetchedAt < settings.MetadataRefreshInterval;
        if (canReusePrevious)
        {
            MetadataState cached = MetadataState.FromPrevious(previous!);
            AddCachedIdentityError(current, cached, errors);
            return cached;
        }

        if (sourceTmdbId is not int tmdbId)
        {
            errors.Add(Error(current.Ids.TraktId, null, "identity", "tmdb_missing"));
            return MetadataState.FromSource(
                current,
                now,
                current.Ids.TvdbId is > 0
                    ? TvIdentityStatus.Verified
                    : TvIdentityStatus.Missing,
                current.Ids.TvdbId,
                null);
        }

        try
        {
            TmdbTvMetadataDto metadata = await metadataClient.GetTvMetadataAsync(
                tmdbId,
                cancellationToken);
            ValidateMetadata(metadata, tmdbId);
            (TvIdentityStatus status, int? resolvedTvdbId, string? identityError) =
                ResolveTvdbIdentity(current.Ids.TvdbId, metadata.ExternalIds.TvdbId);
            if (identityError is not null)
            {
                errors.Add(Error(current.Ids.TraktId, tmdbId, "identity", identityError));
            }

            return MetadataState.FromTmdb(
                current,
                metadata,
                now,
                status,
                resolvedTvdbId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private async Task<TvProviderAvailability> ResolveShowAvailabilityAsync(
        long traktId,
        int? tmdbId,
        TvProviderAvailability? previous,
        DateTimeOffset now,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (!NeedsProviderRefresh(previous, now))
        {
            return previous!;
        }

        if (tmdbId is not int exactTmdbId)
        {
            return TvProviderAvailability.Unknown(settings.ProviderRegion);
        }

        try
        {
            TmdbTvProviderDataDto providers = await metadataClient.GetTvProvidersAsync(
                exactTmdbId,
                cancellationToken);
            return MapAvailability(providers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (TryGetErrorCode(exception, out string? code))
        {
            errors.Add(Error(traktId, exactTmdbId, "show_providers", code!));
            return ProviderFailureFallback(previous, now);
        }
    }

    private async Task<IReadOnlyDictionary<int, TvProviderAvailability>> ResolveSeasonAvailabilityAsync(
        long traktId,
        int? tmdbId,
        IReadOnlyList<int> seasonNumbers,
        TvShow? previous,
        DateTimeOffset now,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        Dictionary<int, TvProviderAvailability> priorBySeason = BuildPreviousSeasonIndex(previous);
        Dictionary<int, TvProviderAvailability> result = [];
        foreach (int seasonNumber in seasonNumbers)
        {
            priorBySeason.TryGetValue(seasonNumber, out TvProviderAvailability? prior);
            if (!NeedsProviderRefresh(prior, now))
            {
                result.Add(seasonNumber, prior!);
                continue;
            }

            if (tmdbId is not int exactTmdbId)
            {
                result.Add(seasonNumber, TvProviderAvailability.Unknown(settings.ProviderRegion));
                continue;
            }

            try
            {
                TmdbTvProviderDataDto providers = await metadataClient.GetSeasonProvidersAsync(
                    exactTmdbId,
                    seasonNumber,
                    cancellationToken);
                result.Add(seasonNumber, MapAvailability(providers));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (TryGetErrorCode(exception, out string? code))
            {
                errors.Add(Error(
                    traktId,
                    exactTmdbId,
                    $"season_{seasonNumber}_providers",
                    code!));
                result.Add(seasonNumber, ProviderFailureFallback(prior, now));
            }
        }

        return result;
    }

    private TvProviderAvailability MapAvailability(TmdbTvProviderDataDto providers)
    {
        if (!StringComparer.Ordinal.Equals(providers.Region, settings.ProviderRegion))
        {
            throw new TmdbParseException("TMDB provider region did not match the configured region.");
        }

        if (providers.RegionPresence == TmdbProviderRegionPresence.Missing)
        {
            return TvProviderAvailability.Unknown(settings.ProviderRegion);
        }

        HashSet<int> ownedProviderIds = settings.OwnedProviderIds.ToHashSet();
        List<TvProviderOffer> offers = providers.Offers
            .Where(offer => ownedProviderIds.Contains(offer.ProviderId))
            .Select(offer => new TvProviderOffer(
                offer.ProviderId,
                offer.ProviderName,
                MapCategory(offer.Category),
                offer.LogoPath))
            .ToList();
        TvProviderState state = offers.Count > 0
            ? TvProviderState.Available
            : TvProviderState.ConfirmedUnavailable;
        return new TvProviderAvailability(
            state,
            settings.ProviderRegion,
            providers.FetchedAt,
            providers.Link,
            offers);
    }

    private TvProviderAvailability ProviderFailureFallback(
        TvProviderAvailability? previous,
        DateTimeOffset now)
    {
        if (previous is not null
            && !StringComparer.Ordinal.Equals(previous.Region, settings.ProviderRegion))
        {
            return TvProviderAvailability.Unknown(settings.ProviderRegion);
        }

        return previous switch
        {
            { FetchedAt: DateTimeOffset fetchedAt } prior
                when fetchedAt <= now
                    && now - fetchedAt <= settings.ProviderCacheLifetime => prior,
            { Offers.Count: > 0 } prior => prior with { State = TvProviderState.Stale },
            _ => TvProviderAvailability.Unknown(settings.ProviderRegion)
        };
    }

    private bool NeedsProviderRefresh(TvProviderAvailability? availability, DateTimeOffset now)
    {
        return availability is null
            || !StringComparer.Ordinal.Equals(availability.Region, settings.ProviderRegion)
            || availability.FetchedAt is not DateTimeOffset fetchedAt
            || fetchedAt > now
            || now - fetchedAt > settings.ProviderCacheLifetime;
    }

    private static Dictionary<int, TvProviderAvailability> BuildPreviousSeasonIndex(TvShow? previous)
    {
        Dictionary<int, TvProviderAvailability> result = [];
        if (previous is null)
        {
            return result;
        }

        foreach (TvSeasonProgress? season in previous.Seasons)
        {
            if (season is null
                || season.SeasonNumber <= 0
                || season.Availability is null
                || !result.TryAdd(season.SeasonNumber, season.Availability))
            {
                throw new ArgumentException("Previous TV season availability is invalid.", nameof(previous));
            }
        }

        return result;
    }

    private static (TvIdentityStatus Status, int? TvdbId, string? ErrorCode) ResolveTvdbIdentity(
        int? traktTvdbId,
        int? tmdbTvdbId)
    {
        if (traktTvdbId is int sourceId && tmdbTvdbId is int externalId)
        {
            return sourceId == externalId
                ? (TvIdentityStatus.Verified, sourceId, null)
                : (TvIdentityStatus.Conflict, sourceId, "tvdb_conflict");
        }

        if (traktTvdbId is int sourceOnlyId)
        {
            return (TvIdentityStatus.Verified, sourceOnlyId, null);
        }

        if (tmdbTvdbId is int resolvedId)
        {
            return (TvIdentityStatus.Verified, resolvedId, null);
        }

        return (TvIdentityStatus.Missing, traktTvdbId, "tvdb_missing");
    }

    private static bool IsTvdbSourceCompatible(int? currentTvdbId, TvShow previous)
    {
        if (currentTvdbId is int sourceTvdbId)
        {
            return previous.IdentityStatus switch
            {
                TvIdentityStatus.Verified or TvIdentityStatus.Conflict =>
                    previous.TvdbId == sourceTvdbId,
                _ => false
            };
        }

        return previous.IdentityStatus switch
        {
            TvIdentityStatus.Verified => previous.TvdbId is > 0,
            TvIdentityStatus.Missing => previous.TvdbId is null,
            _ => false
        };
    }

    private static void AddCachedIdentityError(
        TraktShowMetadata current,
        MetadataState cached,
        List<string> errors)
    {
        string? errorCode = cached.IdentityStatus switch
        {
            _ when current.Ids.TmdbId is null => "tmdb_missing",
            TvIdentityStatus.Conflict => "tvdb_conflict",
            TvIdentityStatus.Missing => "tvdb_missing",
            _ => null
        };
        if (errorCode is not null)
        {
            errors.Add(Error(current.Ids.TraktId, current.Ids.TmdbId, "identity", errorCode));
        }
    }

    private static TvProviderCategory MapCategory(string category)
    {
        return category switch
        {
            "flatrate" => TvProviderCategory.Flatrate,
            "free" => TvProviderCategory.Free,
            "ads" => TvProviderCategory.Ads,
            "rent" => TvProviderCategory.Rent,
            "buy" => TvProviderCategory.Buy,
            _ => throw new TmdbParseException("TMDB provider category was invalid.")
        };
    }

    private static bool TryGetErrorCode(Exception exception, out string? code)
    {
        code = exception switch
        {
            TmdbUnavailableException => "tmdb_unavailable",
            TmdbParseException => "tmdb_parse_error",
            TmdbTvNotFoundException => "tmdb_not_found",
            _ => null
        };
        return code is not null;
    }

    private static void ValidateInputs(
        TraktShowMetadata current,
        IReadOnlyList<int> seasonNumbers,
        TvShow? previous,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(current.Ids);
        ArgumentNullException.ThrowIfNull(seasonNumbers);
        TmdbContractValidation.EnsureUtc(now, nameof(now));
        if (current.Ids.TraktId <= 0
            || current.Ids.TvdbId is <= 0
            || current.Ids.TmdbId is <= 0
            || string.IsNullOrWhiteSpace(current.Title)
            || current.Year is <= 0)
        {
            throw new ArgumentException("Current Trakt show is invalid.", nameof(current));
        }

        if (previous is not null && previous.TraktId != current.Ids.TraktId)
        {
            throw new ArgumentException("Previous TV show identity does not match current Trakt show.", nameof(previous));
        }

        HashSet<int> uniqueSeasons = [];
        if (seasonNumbers.Any(season => season <= 0 || !uniqueSeasons.Add(season)))
        {
            throw new ArgumentException("Season numbers must be positive and unique.", nameof(seasonNumbers));
        }
    }

    private static void ValidateMetadata(TmdbTvMetadataDto metadata, int expectedTmdbId)
    {
        if (metadata is null
            || metadata.TmdbId != expectedTmdbId
            || string.IsNullOrWhiteSpace(metadata.Name)
            || string.IsNullOrWhiteSpace(metadata.OriginalName)
            || metadata.ExternalIds is null
            || metadata.ExternalIds.TvdbId is <= 0)
        {
            throw new TmdbParseException("TMDB TV metadata was invalid.");
        }
    }

    private static string Error(long traktId, int? tmdbId, string stage, string code)
    {
        return $"trakt_id={traktId};tmdb_id={tmdbId?.ToString() ?? "missing"};stage={stage};code={code}";
    }

    private sealed record MetadataState(
        int? TvdbId,
        int? TmdbId,
        string? ImdbId,
        TvIdentityStatus IdentityStatus,
        string Title,
        int? Year,
        string? Overview,
        string? PosterUrl,
        string? BackdropUrl,
        DateTimeOffset FetchedAt)
    {
        public static MetadataState FromPrevious(TvShow previous)
        {
            return new MetadataState(
                previous.TvdbId,
                previous.TmdbId,
                previous.ImdbId,
                previous.IdentityStatus,
                previous.Title,
                previous.Year,
                previous.Overview,
                previous.PosterUrl,
                previous.BackdropUrl,
                previous.MetadataFetchedAt);
        }

        public static MetadataState FromSource(
            TraktShowMetadata current,
            DateTimeOffset fetchedAt,
            TvIdentityStatus identityStatus,
            int? tvdbId,
            int? tmdbId)
        {
            return new MetadataState(
                tvdbId,
                tmdbId,
                NormalizeImdb(current.Ids.ImdbId),
                identityStatus,
                current.Title,
                current.Year,
                current.Overview,
                null,
                null,
                fetchedAt);
        }

        public static MetadataState FromTmdb(
            TraktShowMetadata current,
            TmdbTvMetadataDto metadata,
            DateTimeOffset fetchedAt,
            TvIdentityStatus identityStatus,
            int? tvdbId)
        {
            return new MetadataState(
                tvdbId,
                metadata.TmdbId,
                NormalizeImdb(metadata.ExternalIds.ImdbId) ?? NormalizeImdb(current.Ids.ImdbId),
                identityStatus,
                metadata.Name,
                ParseYear(metadata.FirstAirDate) ?? current.Year,
                metadata.Overview ?? current.Overview,
                metadata.PosterUrl,
                metadata.BackdropUrl,
                fetchedAt);
        }

        private static int? ParseYear(string? date)
        {
            return DateOnly.TryParse(date, out DateOnly parsed) ? parsed.Year : null;
        }

        private static string? NormalizeImdb(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
    }
}
