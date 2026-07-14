using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class TraktConnectionTests
{
    [Fact]
    public void ToString_WhenConnectionContainsSecrets_RedactsSensitiveValues()
    {
        string protectedDeviceCode = "protected-device-sentinel-b18c8e";
        string userCode = "user-code-sentinel-781a5d";
        string protectedAccessToken = "protected-access-sentinel-02dbb1";
        string protectedRefreshToken = "protected-refresh-sentinel-48f31e";
        TraktConnection connection = new(
            "connected",
            protectedDeviceCode,
            userCode,
            "https://trakt.tv/activate",
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
            TimeSpan.FromSeconds(5),
            DateTimeOffset.Parse("2026-07-14T10:00:05Z"),
            protectedAccessToken,
            protectedRefreshToken,
            DateTimeOffset.Parse("2026-10-14T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-14T10:00:00Z"));

        string diagnostic = connection.ToString();

        diagnostic.Should().Be("TraktConnection { State = connected }");
        diagnostic.Should().NotContain(protectedDeviceCode);
        diagnostic.Should().NotContain(userCode);
        diagnostic.Should().NotContain(protectedAccessToken);
        diagnostic.Should().NotContain(protectedRefreshToken);
    }
}
