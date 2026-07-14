using FluentAssertions;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class DomainEnumTests
{
    [Fact]
    public void DomainEnums_DefaultValues_AreReservedForUnspecifiedOrUnknownStates()
    {
        ((int)MediaType.Unspecified).Should().Be(0);
        ((int)MediaType.Movie).Should().Be(1);
        ((int)MediaType.TvShow).Should().Be(2);

        ((int)WatchlistSource.Unspecified).Should().Be(0);
        ((int)WatchlistSource.Letterboxd).Should().Be(1);
        ((int)WatchlistSource.Tmdb).Should().Be(2);

        ((int)ReleaseStatus.Unknown).Should().Be(0);
        ((int)ReleaseStatus.Released).Should().Be(1);
        ((int)ReleaseStatus.Unreleased).Should().Be(2);

        ((int)AvailabilityStatus.Unspecified).Should().Be(0);
        ((int)AvailabilityStatus.AvailableOnPlex).Should().Be(1);
        ((int)AvailabilityStatus.NotOnPlex).Should().Be(2);
        ((int)AvailabilityStatus.Unreleased).Should().Be(3);
        ((int)AvailabilityStatus.UnknownMatch).Should().Be(4);
    }

    [Fact]
    public void TvDomainEnums_ValuesAndOrder_AreStable()
    {
        Enum.GetValues<TvLifecycleState>().Should().Equal(
            TvLifecycleState.Active,
            TvLifecycleState.CaughtUp,
            TvLifecycleState.SourceRemoved,
            TvLifecycleState.TerminalCleanupPending,
            TvLifecycleState.RetiredTerminal);

        Enum.GetValues<TvIdentityStatus>().Should().Equal(
            TvIdentityStatus.Verified,
            TvIdentityStatus.Missing,
            TvIdentityStatus.Conflict,
            TvIdentityStatus.LegacyUnresolved);

        Enum.GetValues<TvProviderState>().Should().Equal(
            TvProviderState.Available,
            TvProviderState.ConfirmedUnavailable,
            TvProviderState.Unknown,
            TvProviderState.Stale);

        Enum.GetValues<TvProviderCategory>().Should().Equal(
            TvProviderCategory.Flatrate,
            TvProviderCategory.Free,
            TvProviderCategory.Ads,
            TvProviderCategory.Rent,
            TvProviderCategory.Buy);

        Enum.GetValues<TvGenerationKind>().Should().Equal(
            TvGenerationKind.ScheduledFull,
            TvGenerationKind.ActivityFull);

        ((int)TvLifecycleState.Active).Should().Be(0);
        ((int)TvLifecycleState.CaughtUp).Should().Be(1);
        ((int)TvLifecycleState.SourceRemoved).Should().Be(2);
        ((int)TvLifecycleState.TerminalCleanupPending).Should().Be(3);
        ((int)TvLifecycleState.RetiredTerminal).Should().Be(4);

        ((int)TvIdentityStatus.Verified).Should().Be(0);
        ((int)TvIdentityStatus.Missing).Should().Be(1);
        ((int)TvIdentityStatus.Conflict).Should().Be(2);
        ((int)TvIdentityStatus.LegacyUnresolved).Should().Be(3);

        ((int)TvProviderState.Available).Should().Be(0);
        ((int)TvProviderState.ConfirmedUnavailable).Should().Be(1);
        ((int)TvProviderState.Unknown).Should().Be(2);
        ((int)TvProviderState.Stale).Should().Be(3);

        ((int)TvProviderCategory.Flatrate).Should().Be(0);
        ((int)TvProviderCategory.Free).Should().Be(1);
        ((int)TvProviderCategory.Ads).Should().Be(2);
        ((int)TvProviderCategory.Rent).Should().Be(3);
        ((int)TvProviderCategory.Buy).Should().Be(4);

        ((int)TvGenerationKind.ScheduledFull).Should().Be(0);
        ((int)TvGenerationKind.ActivityFull).Should().Be(1);
    }
}
