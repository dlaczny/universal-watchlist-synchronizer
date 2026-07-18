using FluentAssertions;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TmdbTvEnrichmentServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z");

    public static TheoryData<int?, int?, TvIdentityStatus, int?> IdentityCases => new()
    {
        { 121361, 121361, TvIdentityStatus.Verified, 121361 },
        { 121361, 999999, TvIdentityStatus.Conflict, 121361 },
        { 121361, null, TvIdentityStatus.Verified, 121361 },
        { null, 121361, TvIdentityStatus.Verified, 121361 },
        { null, null, TvIdentityStatus.Missing, null }
    };

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public async Task EnrichAsync_ReconcilesExactTvdbIdentityWithoutOmittingShow(
        int? traktTvdbId,
        int? tmdbTvdbId,
        TvIdentityStatus expectedStatus,
        int? expectedTvdbId)
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(tmdbTvdbId);
        TmdbTvEnrichmentService service = CreateService(client);
        TraktShowMetadata source = SourceShow(traktTvdbId);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            source,
            [],
            null,
            Now,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Title.Should().Be("Game of Thrones");
        result.TmdbId.Should().Be(1399);
        result.TvdbId.Should().Be(expectedTvdbId);
        result.IdentityStatus.Should().Be(expectedStatus);
        if (expectedStatus == TvIdentityStatus.Conflict)
        {
            result.Errors.Should().ContainSingle().Which.Should().Be(
                "trakt_id=12345;tmdb_id=1399;stage=identity;code=tvdb_conflict");
        }
        else if (expectedStatus == TvIdentityStatus.Missing)
        {
            result.Errors.Should().ContainSingle().Which.Should().Be(
                "trakt_id=12345;tmdb_id=1399;stage=identity;code=tvdb_missing");
        }
        else
        {
            result.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task EnrichAsync_UsesStableConfiguredProviderIdsAndPreservesEveryCategory()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviders = ProviderData(
            [
                new TmdbTvProviderOfferDto(119, "Completely renamed", "flatrate", "/119.jpg"),
                new TmdbTvProviderOfferDto(1899, "Channel", "free", "/1899.jpg"),
                new TmdbTvProviderOfferDto(1773, "Sky", "ads", "/1773.jpg"),
                new TmdbTvProviderOfferDto(119, "Completely renamed", "rent", "/119-rent.jpg"),
                new TmdbTvProviderOfferDto(8, "Unowned", "buy", "/8.jpg")
            ]);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            null,
            Now,
            CancellationToken.None);

        result.Availability.State.Should().Be(TvProviderState.Available);
        result.Availability.Region.Should().Be("PL");
        result.Availability.FetchedAt.Should().Be(Now);
        result.Availability.Link.Should().Be("https://tmdb.example/watch");
        result.Availability.Offers.Should().Equal(
            new TvProviderOffer(119, "Completely renamed", TvProviderCategory.Flatrate, "/119.jpg"),
            new TvProviderOffer(1899, "Channel", TvProviderCategory.Free, "/1899.jpg"),
            new TvProviderOffer(1773, "Sky", TvProviderCategory.Ads, "/1773.jpg"),
            new TvProviderOffer(119, "Completely renamed", TvProviderCategory.Rent, "/119-rent.jpg"));
    }

    [Fact]
    public async Task EnrichAsync_WhenPlPresentWithoutConfiguredIds_ReturnsConfirmedUnavailable()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviders = ProviderData(
            [new TmdbTvProviderOfferDto(8, "Netflix", "flatrate", "/8.jpg")]);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            null,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(new TvProviderAvailability(
            TvProviderState.ConfirmedUnavailable,
            "PL",
            Now,
            "https://tmdb.example/watch",
            []));
    }

    [Fact]
    public async Task EnrichAsync_WhenPlRegionMissing_ReturnsUnknownNotConfirmedUnavailable()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviders = new TmdbTvProviderDataDto(
            "PL",
            TmdbProviderRegionPresence.Missing,
            Now,
            null,
            []);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            null,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
    }

    [Fact]
    public async Task EnrichAsync_WhenProviderUnavailableWithoutPreviousData_ReturnsUnknownAndRedactedError()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        string secret = "access-token-query-and-body-sentinel";
        client.ShowProviderException = new TmdbUnavailableException(secret);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            null,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "trakt_id=12345;tmdb_id=1399;stage=show_providers;code=tmdb_unavailable");
        string rendered = string.Join(Environment.NewLine, result.Errors);
        rendered.Should().NotContain(secret);
    }

    [Fact]
    public async Task EnrichAsync_WhenProviderRefreshFailsWithOldOffers_MarksPriorDataStale()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviderException = new TmdbUnavailableException("raw-body-sentinel");
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-25),
            [new TvProviderOffer(119, "Prior", TvProviderCategory.Flatrate, "/prior.jpg")]);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(prior with { State = TvProviderState.Stale });
        result.Availability.Offers.Should().Equal(prior.Offers);
    }

    [Fact]
    public async Task EnrichAsync_WhenExpiredConfirmedUnavailableRefreshFails_ReturnsUnknown()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviderException = new TmdbUnavailableException("failure");
        TvShow previous = PreviousShow(
            Availability(TvProviderState.ConfirmedUnavailable, Now.AddHours(-25), []),
            Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
    }

    [Theory]
    [InlineData(TvProviderState.Available)]
    [InlineData(TvProviderState.ConfirmedUnavailable)]
    public async Task EnrichAsync_WhenProviderRefreshFailsWithFreshPrior_PreservesConfirmedState(
        TvProviderState state)
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviderException = new TmdbUnavailableException("failure");
        IReadOnlyList<TvProviderOffer> offers = state == TvProviderState.Available
            ? [new TvProviderOffer(119, "Prior", TvProviderCategory.Flatrate, null)]
            : [];
        TvProviderAvailability prior = Availability(state, Now.AddHours(-23), offers);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(prior);
    }

    [Fact]
    public async Task EnrichAsync_MapsShowAndEachNumberedSeasonIndependently()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.SeasonProviders[1] = ProviderData(
            [new TmdbTvProviderOfferDto(119, "Max", "flatrate", null)]);
        client.SeasonProviders[2] = ProviderData(
            [new TmdbTvProviderOfferDto(8, "Netflix", "flatrate", null)]);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [2, 1],
            null,
            Now,
            CancellationToken.None);

        client.SeasonRequests.Should().Equal((1399, 1), (1399, 2));
        result.SeasonAvailability.Keys.Should().Equal(1, 2);
        result.SeasonAvailability[1].State.Should().Be(TvProviderState.Available);
        result.SeasonAvailability[2].State.Should().Be(TvProviderState.ConfirmedUnavailable);
    }

    [Fact]
    public async Task EnrichAsync_WhenOneSeasonProviderFails_IsolatesFailureFromOtherSeason()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.SeasonProviderExceptions[1] = new TmdbUnavailableException("season-one-body");
        client.SeasonProviders[2] = ProviderData(
            [new TmdbTvProviderOfferDto(119, "Max", "flatrate", null)]);
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [1, 2],
            null,
            Now,
            CancellationToken.None);

        result.SeasonAvailability[1].State.Should().Be(TvProviderState.Unknown);
        result.SeasonAvailability[2].State.Should().Be(TvProviderState.Available);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "trakt_id=12345;tmdb_id=1399;stage=season_1_providers;code=tmdb_unavailable");
    }

    [Fact]
    public async Task EnrichAsync_WhenCallerCancelsProviderRead_DoesNotFallbackOrRecordError()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        OperationCanceledException cancellation = new(source.Token);
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviderException = cancellation;
        TmdbTvEnrichmentService service = CreateService(client);

        Func<Task> action = () => service.EnrichAsync(
            SourceShow(121361),
            [],
            null,
            Now,
            source.Token);

        OperationCanceledException thrown = (await action.Should()
            .ThrowAsync<OperationCanceledException>())
            .Which;
        thrown.Should().BeSameAs(cancellation);
    }

    [Fact]
    public async Task EnrichAsync_WhenPreviousMetadataAndProvidersFresh_DoesNotRefreshAndSnapshotsResults()
    {
        FakeTmdbTvMetadataClient client = new();
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-1),
            [new TvProviderOffer(119, "Prior", TvProviderCategory.Flatrate, null)]);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);
        List<int> seasons = [1];

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            seasons,
            previous,
            Now,
            CancellationToken.None);
        seasons.Add(2);

        client.MetadataRequests.Should().BeEmpty();
        client.ShowProviderRequests.Should().BeEmpty();
        client.SeasonRequests.Should().BeEmpty();
        result.Title.Should().Be(previous.Title);
        result.MetadataFetchedAt.Should().Be(previous.MetadataFetchedAt);
        result.Availability.Should().Be(prior);
        result.SeasonAvailability.Keys.Should().Equal(1);
        Action mutate = () => ((IDictionary<int, TvProviderAvailability>)result.SeasonAvailability)
            .Add(2, prior);
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task EnrichAsync_WhenFreshPriorTvdbDiffersFromCurrentTraktTvdb_RefreshesIdentity()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        TvProviderAvailability prior = Availability(
            TvProviderState.ConfirmedUnavailable,
            Now.AddHours(-1),
            []);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(999999),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().Equal(1399);
        result.IdentityStatus.Should().Be(TvIdentityStatus.Conflict);
        result.TvdbId.Should().Be(999999);
        result.Errors.Should().ContainSingle().Which.Should().Contain("code=tvdb_conflict");
    }

    [Theory]
    [InlineData(TvIdentityStatus.Conflict, 999999, 999999, "tvdb_conflict")]
    [InlineData(TvIdentityStatus.Missing, null, null, "tvdb_missing")]
    public async Task EnrichAsync_WhenUnresolvedIdentityCacheIsReusable_RetainsStableIdentityError(
        TvIdentityStatus priorStatus,
        int? previousTvdbId,
        int? currentTvdbId,
        string expectedCode)
    {
        FakeTmdbTvMetadataClient client = new();
        TvProviderAvailability prior = Availability(
            TvProviderState.ConfirmedUnavailable,
            Now.AddHours(-1),
            []);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1)) with
        {
            TvdbId = previousTvdbId,
            IdentityStatus = priorStatus
        };
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(currentTvdbId),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().BeEmpty();
        result.IdentityStatus.Should().Be(priorStatus);
        result.Errors.Should().ContainSingle().Which.Should().Be(
            $"trakt_id=12345;tmdb_id=1399;stage=identity;code={expectedCode}");
    }

    [Fact]
    public async Task EnrichAsync_WhenFreshPriorIsLegacyUnresolved_RefreshesExactIdentity()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        TvProviderAvailability prior = Availability(
            TvProviderState.ConfirmedUnavailable,
            Now.AddHours(-1),
            []);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1)) with
        {
            IdentityStatus = TvIdentityStatus.LegacyUnresolved
        };
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().Equal(1399);
        result.IdentityStatus.Should().Be(TvIdentityStatus.Verified);
    }

    [Fact]
    public async Task EnrichAsync_WhenFreshPriorMissingHasPositiveTraktTvdb_RepairsIdentity()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(null);
        TvProviderAvailability prior = Availability(
            TvProviderState.ConfirmedUnavailable,
            Now.AddHours(-1),
            []);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1)) with
        {
            IdentityStatus = TvIdentityStatus.Missing
        };
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().Equal(1399);
        result.IdentityStatus.Should().Be(TvIdentityStatus.Verified);
        result.TvdbId.Should().Be(121361);
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TvIdentityStatus.Conflict)]
    [InlineData(TvIdentityStatus.Missing)]
    public async Task EnrichAsync_WhenCurrentTraktTvdbIsRemovedFromUnresolvedFreshPrior_RefreshesIdentity(
        TvIdentityStatus priorStatus)
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        TvProviderAvailability prior = Availability(
            TvProviderState.ConfirmedUnavailable,
            Now.AddHours(-1),
            []);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1)) with
        {
            TvdbId = 999999,
            IdentityStatus = priorStatus
        };
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(null),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().Equal(1399);
        result.IdentityStatus.Should().Be(TvIdentityStatus.Verified);
        result.TvdbId.Should().Be(121361);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WhenTmdbIdentityChanges_DoesNotReuseOldShowOrSeasonAvailability()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361, 1400);
        client.ShowProviders = ProviderData(
            [new TmdbTvProviderOfferDto(8, "Unowned", "flatrate", null)]);
        client.SeasonProviders[1] = ProviderData(
            [new TmdbTvProviderOfferDto(119, "New identity provider", "flatrate", null)]);
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-1),
            [new TvProviderOffer(119, "Old identity provider", TvProviderCategory.Flatrate, null)]);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);
        TraktShowMetadata changed = new(
            new TraktShowIds(12345, 121361, 1400, "tt0944947"),
            "Source title",
            2011,
            "Source overview",
            "ended");

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            changed,
            [1],
            previous,
            Now,
            CancellationToken.None);

        client.ShowProviderRequests.Should().Equal(1400);
        client.SeasonRequests.Should().Equal((1400, 1));
        result.Availability.State.Should().Be(TvProviderState.ConfirmedUnavailable);
        result.SeasonAvailability[1].Offers.Should().ContainSingle()
            .Which.ProviderName.Should().Be("New identity provider");
    }

    [Fact]
    public async Task EnrichAsync_WhenFreshPriorRegionDiffers_RefreshesShowAndSeasonForPoland()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.SeasonProviders[1] = ProviderData([]);
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-1),
            [new TvProviderOffer(119, "Foreign claim", TvProviderCategory.Flatrate, null)],
            "DE");
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [1],
            previous,
            Now,
            CancellationToken.None);

        client.ShowProviderRequests.Should().Equal(1399);
        client.SeasonRequests.Should().Equal((1399, 1));
        result.Availability.Region.Should().Be("PL");
        result.SeasonAvailability[1].Region.Should().Be("PL");
        result.Availability.Offers.Should().BeEmpty();
        result.SeasonAvailability[1].Offers.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WhenExpiredForeignRegionRefreshFails_ReturnsPolandUnknown()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        client.ShowProviderException = new TmdbUnavailableException("failure");
        client.SeasonProviderExceptions[1] = new TmdbUnavailableException("failure");
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-25),
            [new TvProviderOffer(119, "Foreign claim", TvProviderCategory.Flatrate, null)],
            "DE");
        TvShow previous = PreviousShow(prior, Now.AddHours(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [1],
            previous,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
        result.SeasonAvailability[1].Should().Be(TvProviderAvailability.Unknown("PL"));
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnrichAsync_WhenMetadataRefreshIsDue_UsesExactTmdbIdentityAndArtwork()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        TvShow previous = PreviousShow(
            Availability(TvProviderState.ConfirmedUnavailable, Now.AddHours(-1), []),
            Now.AddDays(-1));
        TmdbTvEnrichmentService service = CreateService(client);

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            SourceShow(121361),
            [],
            previous,
            Now,
            CancellationToken.None);

        client.MetadataRequests.Should().Equal(1399);
        result.Title.Should().Be("Game of Thrones");
        result.Year.Should().Be(2011);
        result.Overview.Should().Be("Overview");
        result.PosterUrl.Should().Be("https://image.example/poster.jpg");
        result.BackdropUrl.Should().Be("https://image.example/backdrop.jpg");
        result.MetadataFetchedAt.Should().Be(Now);
    }

    [Fact]
    public async Task EnrichAsync_WhenRequiredMetadataRefreshFails_PropagatesTypedFailure()
    {
        TmdbUnavailableException failure = new("fixed dependency error");
        FakeTmdbTvMetadataClient client = new()
        {
            MetadataException = failure
        };
        TvShow previous = PreviousShow(
            Availability(TvProviderState.Available, Now.AddHours(-1), []),
            Now.AddDays(-2));
        TmdbTvEnrichmentService service = CreateService(client);

        Func<Task> action = () => service.EnrichAsync(
            SourceShow(121361),
            [1],
            previous,
            Now,
            CancellationToken.None);

        TmdbUnavailableException thrown = (await action.Should()
            .ThrowAsync<TmdbUnavailableException>())
            .Which;
        thrown.Should().BeSameAs(failure);
        client.ShowProviderRequests.Should().BeEmpty();
        client.SeasonRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WhenTmdbIdentityInvalid_StaysVisibleWithMissingIdentityAndNoNetworkCall()
    {
        FakeTmdbTvMetadataClient client = new();
        TmdbTvEnrichmentService service = CreateService(client);
        TraktShowMetadata source = new(
            new TraktShowIds(12345, null, null, "tt0944947"),
            "Source title",
            2011,
            "Source overview",
            "ended");

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            source,
            [],
            null,
            Now,
            CancellationToken.None);

        result.Title.Should().Be("Source title");
        result.IdentityStatus.Should().Be(TvIdentityStatus.Missing);
        result.TmdbId.Should().BeNull();
        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
        client.MetadataRequests.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("code=tmdb_missing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrichAsync_WhenTmdbIdMissingButTraktTvdbIsPositive_UsesVerifiedSourceIdentity(
        bool useFreshPrior)
    {
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-1),
            [new TvProviderOffer(119, "Old claim", TvProviderCategory.Flatrate, null)]);
        TvShow? previous = useFreshPrior
            ? PreviousShow(prior, Now.AddHours(-1)) with { TmdbId = null }
            : null;
        FakeTmdbTvMetadataClient client = new();
        TmdbTvEnrichmentService service = CreateService(client);
        TraktShowMetadata source = new(
            new TraktShowIds(12345, 121361, null, "tt0944947"),
            "Source title",
            2011,
            "Source overview",
            "ended");

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            source,
            [],
            previous,
            Now,
            CancellationToken.None);

        result.IdentityStatus.Should().Be(TvIdentityStatus.Verified);
        result.TvdbId.Should().Be(121361);
        result.TmdbId.Should().BeNull();
        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
        result.Errors.Should().ContainSingle().Which.Should().Be(
            "trakt_id=12345;tmdb_id=missing;stage=identity;code=tmdb_missing");
        client.MetadataRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_WithoutPositiveTmdbIdentity_DoesNotReusePriorProviderClaims()
    {
        TvProviderAvailability prior = Availability(
            TvProviderState.Available,
            Now.AddHours(-1),
            [new TvProviderOffer(119, "Old claim", TvProviderCategory.Flatrate, null)]);
        TvShow previous = PreviousShow(prior, Now.AddHours(-1)) with
        {
            TmdbId = null,
            TvdbId = null,
            IdentityStatus = TvIdentityStatus.Missing
        };
        FakeTmdbTvMetadataClient client = new();
        TmdbTvEnrichmentService service = CreateService(client);
        TraktShowMetadata source = new(
            new TraktShowIds(12345, null, null, "tt0944947"),
            "Source title",
            2011,
            "Source overview",
            "ended");

        TmdbTvEnrichmentResult result = await service.EnrichAsync(
            source,
            [1],
            previous,
            Now,
            CancellationToken.None);

        result.Availability.Should().Be(TvProviderAvailability.Unknown("PL"));
        result.SeasonAvailability[1].Should().Be(TvProviderAvailability.Unknown("PL"));
        client.ShowProviderRequests.Should().BeEmpty();
        client.SeasonRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichAsync_RejectsDuplicateOrNonPositiveSeasonNumbersBeforeSideEffects()
    {
        FakeTmdbTvMetadataClient client = CreateSuccessfulClient(121361);
        TmdbTvEnrichmentService service = CreateService(client);

        Func<Task> duplicate = () => service.EnrichAsync(
            SourceShow(121361),
            [1, 1],
            null,
            Now,
            CancellationToken.None);
        Func<Task> nonPositive = () => service.EnrichAsync(
            SourceShow(121361),
            [0],
            null,
            Now,
            CancellationToken.None);

        await duplicate.Should().ThrowAsync<ArgumentException>();
        await nonPositive.Should().ThrowAsync<ArgumentException>();
        client.MetadataRequests.Should().BeEmpty();
    }

    [Fact]
    public void ProviderDto_WhenCombinationImpossible_RejectsBeforeUse()
    {
        Action missingWithOffers = () => _ = new TmdbTvProviderDataDto(
            "PL",
            TmdbProviderRegionPresence.Missing,
            Now,
            "https://should-not-exist",
            [new TmdbTvProviderOfferDto(119, "Max", "flatrate", null)]);
        Action nonUtc = () => _ = new TmdbTvProviderDataDto(
            "PL",
            TmdbProviderRegionPresence.Present,
            DateTimeOffset.Parse("2026-07-18T14:00:00+02:00"),
            null,
            []);
        Action invalidCategory = () => _ = new TmdbTvProviderOfferDto(
            119,
            "Max",
            "subscription",
            null);

        missingWithOffers.Should().Throw<ArgumentException>();
        nonUtc.Should().Throw<ArgumentException>();
        invalidCategory.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnrichmentSettings_SnapshotStablePositiveProviderIdsAndRejectInvalidConfiguration()
    {
        List<int> providerIds = [119, 1899, 1773];
        TmdbEnrichmentSettings settings = new(
            "PL",
            providerIds,
            TimeSpan.FromDays(1),
            TimeSpan.FromHours(24));
        providerIds.Add(8);

        settings.OwnedProviderIds.Should().Equal(119, 1899, 1773);
        Action mutate = () => ((IList<int>)settings.OwnedProviderIds).Add(8);
        Action duplicate = () => _ = new TmdbEnrichmentSettings(
            "PL",
            [119, 119],
            TimeSpan.FromDays(1),
            TimeSpan.FromHours(24));
        Action invalidRegion = () => _ = new TmdbEnrichmentSettings(
            "pl",
            [119],
            TimeSpan.FromDays(1),
            TimeSpan.FromHours(24));

        mutate.Should().Throw<NotSupportedException>();
        duplicate.Should().Throw<ArgumentException>();
        invalidRegion.Should().Throw<ArgumentException>();
    }

    private static TmdbTvEnrichmentService CreateService(ITmdbTvMetadataClient client)
    {
        return new TmdbTvEnrichmentService(
            client,
            new TmdbEnrichmentSettings(
                "PL",
                [119, 1899, 1773],
                TimeSpan.FromDays(1),
                TimeSpan.FromHours(24)));
    }

    private static FakeTmdbTvMetadataClient CreateSuccessfulClient(
        int? externalTvdbId,
        int tmdbId = 1399)
    {
        return new FakeTmdbTvMetadataClient
        {
            Metadata = new TmdbTvMetadataDto(
                tmdbId,
                "Game of Thrones",
                "Game of Thrones",
                "Overview",
                "2011-04-17",
                "Ended",
                "/poster.jpg",
                "/backdrop.jpg",
                "https://image.example/poster.jpg",
                "https://image.example/backdrop.jpg",
                ["Drama"],
                "en",
                8.5,
                25000,
                new TmdbTvExternalIdsDto("tt0944947", externalTvdbId)),
            ShowProviders = ProviderData([])
        };
    }

    private static TraktShowMetadata SourceShow(int? tvdbId)
    {
        return new TraktShowMetadata(
            new TraktShowIds(12345, tvdbId, 1399, "tt0944947"),
            "Source title",
            2011,
            "Source overview",
            "ended");
    }

    private static TmdbTvProviderDataDto ProviderData(
        IReadOnlyList<TmdbTvProviderOfferDto> offers)
    {
        return new TmdbTvProviderDataDto(
            "PL",
            TmdbProviderRegionPresence.Present,
            Now,
            "https://tmdb.example/watch",
            offers);
    }

    private static TvProviderAvailability Availability(
        TvProviderState state,
        DateTimeOffset fetchedAt,
        IReadOnlyList<TvProviderOffer> offers,
        string region = "PL")
    {
        return new TvProviderAvailability(
            state,
            region,
            fetchedAt,
            "https://prior.example/watch",
            offers);
    }

    private static TvShow PreviousShow(
        TvProviderAvailability availability,
        DateTimeOffset metadataFetchedAt)
    {
        return new TvShow(
            "tv-trakt-12345",
            12345,
            121361,
            1399,
            "tt0944947",
            TvIdentityStatus.Verified,
            "Prior title",
            2011,
            "Prior overview",
            "https://prior.example/poster.jpg",
            "https://prior.example/backdrop.jpg",
            "ended",
            true,
            1,
            0,
            null,
            null,
            [
                new TvSeasonProgress(
                    1,
                    1,
                    0,
                    false,
                    availability,
                    [])
            ],
            [],
            availability,
            TvLifecycleState.Active,
            "added",
            1,
            0,
            Now.AddDays(-10),
            Now.AddHours(-1),
            metadataFetchedAt,
            "tv-20260718110000000-11111111111111111111111111111111",
            null);
    }

    private sealed class FakeTmdbTvMetadataClient : ITmdbTvMetadataClient
    {
        public TmdbTvMetadataDto? Metadata { get; init; }

        public Exception? MetadataException { get; init; }

        public TmdbTvProviderDataDto? ShowProviders { get; set; }

        public Exception? ShowProviderException { get; set; }

        public Dictionary<int, TmdbTvProviderDataDto> SeasonProviders { get; } = [];

        public Dictionary<int, Exception> SeasonProviderExceptions { get; } = [];

        public List<int> MetadataRequests { get; } = [];

        public List<int> ShowProviderRequests { get; } = [];

        public List<(int TmdbId, int SeasonNumber)> SeasonRequests { get; } = [];

        public Task<TmdbTvMetadataDto> GetTvMetadataAsync(
            int tmdbId,
            CancellationToken cancellationToken)
        {
            MetadataRequests.Add(tmdbId);
            if (MetadataException is not null)
            {
                return Task.FromException<TmdbTvMetadataDto>(MetadataException);
            }

            return Task.FromResult(Metadata!);
        }

        public Task<TmdbTvProviderDataDto> GetTvProvidersAsync(
            int tmdbId,
            CancellationToken cancellationToken)
        {
            ShowProviderRequests.Add(tmdbId);
            if (ShowProviderException is not null)
            {
                return Task.FromException<TmdbTvProviderDataDto>(ShowProviderException);
            }

            return Task.FromResult(ShowProviders!);
        }

        public Task<TmdbTvProviderDataDto> GetSeasonProvidersAsync(
            int tmdbId,
            int seasonNumber,
            CancellationToken cancellationToken)
        {
            SeasonRequests.Add((tmdbId, seasonNumber));
            if (SeasonProviderExceptions.TryGetValue(seasonNumber, out Exception? exception))
            {
                return Task.FromException<TmdbTvProviderDataDto>(exception);
            }

            return Task.FromResult(SeasonProviders.TryGetValue(seasonNumber, out TmdbTvProviderDataDto? data)
                ? data
                : ProviderData([]));
        }

        public Task<TmdbWatchProviderCatalogDto> GetProviderCatalogAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TmdbWatchProviderRegionsDto> GetProviderRegionsAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
