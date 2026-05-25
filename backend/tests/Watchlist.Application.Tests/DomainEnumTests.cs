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
}
