using FluentAssertions;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvContractImmutabilityTests
{
    private static readonly DateTimeOffset s_timestamp =
        new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TvProviderAvailability_Offers_AreReadOnlySnapshots()
    {
        TvProviderOffer firstOffer = CreateOffer(1);
        TvProviderOffer secondOffer = CreateOffer(2);
        TvProviderOffer thirdOffer = CreateOffer(3);
        List<TvProviderOffer> source = [firstOffer];
        TvProviderAvailability availability = CreateAvailability(source);

        source.Add(secondOffer);

        availability.Offers.Should().Equal(firstOffer);

        List<TvProviderOffer> replacement = [secondOffer];
        TvProviderAvailability copy = availability with { Offers = replacement };
        replacement.Add(thirdOffer);

        copy.Offers.Should().Equal(secondOffer);
        Action mutateExposedList = () => ((IList<TvProviderOffer>)copy.Offers).Add(thirdOffer);
        mutateExposedList.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TvSeasonProgress_Episodes_AreReadOnlySnapshots()
    {
        TvEpisodeProgress firstEpisode = CreateEpisode(1);
        TvEpisodeProgress secondEpisode = CreateEpisode(2);
        TvEpisodeProgress thirdEpisode = CreateEpisode(3);
        List<TvEpisodeProgress> source = [firstEpisode];
        TvSeasonProgress season = CreateSeason(1, source);

        source.Add(secondEpisode);

        season.Episodes.Should().Equal(firstEpisode);

        List<TvEpisodeProgress> replacement = [secondEpisode];
        TvSeasonProgress copy = season with { Episodes = replacement };
        replacement.Add(thirdEpisode);

        copy.Episodes.Should().Equal(secondEpisode);
        Action mutateExposedList = () => ((IList<TvEpisodeProgress>)copy.Episodes).Add(thirdEpisode);
        mutateExposedList.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TvShow_Seasons_AreReadOnlySnapshots()
    {
        TvSeasonProgress firstSeason = CreateSeason(1, []);
        TvSeasonProgress secondSeason = CreateSeason(2, []);
        TvSeasonProgress thirdSeason = CreateSeason(3, []);
        List<TvSeasonProgress> source = [firstSeason];
        TvShow show = CreateShow(source, []);

        source.Add(secondSeason);

        show.Seasons.Should().Equal(firstSeason);

        List<TvSeasonProgress> replacement = [secondSeason];
        TvShow copy = show with { Seasons = replacement };
        replacement.Add(thirdSeason);

        copy.Seasons.Should().Equal(secondSeason);
        Action mutateExposedList = () => ((IList<TvSeasonProgress>)copy.Seasons).Add(thirdSeason);
        mutateExposedList.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TvShow_SpecialEpisodeIdentities_AreReadOnlySnapshots()
    {
        TvSpecialEpisodeIdentity firstSpecial = new(1, 11, 0, 1);
        TvSpecialEpisodeIdentity secondSpecial = new(2, 12, 0, 2);
        TvSpecialEpisodeIdentity thirdSpecial = new(3, 13, 0, 3);
        List<TvSpecialEpisodeIdentity> source = [firstSpecial];
        TvShow show = CreateShow([], source);

        source.Add(secondSpecial);

        show.SpecialEpisodeIdentities.Should().Equal(firstSpecial);

        List<TvSpecialEpisodeIdentity> replacement = [secondSpecial];
        TvShow copy = show with { SpecialEpisodeIdentities = replacement };
        replacement.Add(thirdSpecial);

        copy.SpecialEpisodeIdentities.Should().Equal(secondSpecial);
        Action mutateExposedList = () =>
            ((IList<TvSpecialEpisodeIdentity>)copy.SpecialEpisodeIdentities).Add(thirdSpecial);
        mutateExposedList.Should().Throw<NotSupportedException>();
    }

    private static TvProviderOffer CreateOffer(int providerId)
    {
        return new TvProviderOffer(providerId, $"Provider {providerId}", TvProviderCategory.Flatrate, null);
    }

    private static TvProviderAvailability CreateAvailability(IReadOnlyList<TvProviderOffer> offers)
    {
        return new TvProviderAvailability(TvProviderState.Available, "PL", s_timestamp, null, offers);
    }

    private static TvEpisodeProgress CreateEpisode(int episodeNumber)
    {
        return new TvEpisodeProgress(
            episodeNumber,
            episodeNumber,
            1,
            episodeNumber,
            $"Episode {episodeNumber}",
            s_timestamp,
            false,
            null);
    }

    private static TvSeasonProgress CreateSeason(
        int seasonNumber,
        IReadOnlyList<TvEpisodeProgress> episodes)
    {
        return new TvSeasonProgress(
            seasonNumber,
            episodes.Count,
            0,
            false,
            TvProviderAvailability.Unknown("PL"),
            episodes);
    }

    private static TvShow CreateShow(
        IReadOnlyList<TvSeasonProgress> seasons,
        IReadOnlyList<TvSpecialEpisodeIdentity> specialEpisodeIdentities)
    {
        return new TvShow(
            "trakt:1",
            1,
            2,
            3,
            "tt0000001",
            TvIdentityStatus.Verified,
            "Example Show",
            2026,
            null,
            null,
            null,
            "returning series",
            true,
            1,
            0,
            null,
            null,
            seasons,
            specialEpisodeIdentities,
            TvProviderAvailability.Unknown("PL"),
            TvLifecycleState.Active,
            null,
            0,
            0,
            s_timestamp,
            s_timestamp,
            s_timestamp,
            "generation-1",
            null);
    }
}
