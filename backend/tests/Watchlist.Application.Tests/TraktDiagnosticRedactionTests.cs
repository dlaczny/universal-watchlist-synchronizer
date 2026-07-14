using System.Reflection;
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TraktDiagnosticRedactionTests
{
    [Fact]
    public void TraktDeviceCode_ToString_RedactsDeviceAndUserCodes()
    {
        TraktDeviceCode deviceCode = new(
            "device-code-diagnostic-sentinel",
            "user-code-diagnostic-sentinel",
            "https://trakt.tv/activate",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(5));

        string diagnostic = deviceCode.ToString();

        diagnostic.Should().Be("TraktDeviceCode { Values = [REDACTED] }");
        diagnostic.Should().NotContain("device-code-diagnostic-sentinel");
        diagnostic.Should().NotContain("user-code-diagnostic-sentinel");
    }

    [Fact]
    public void TraktTokenGrant_ToString_RedactsAccessAndRefreshTokens()
    {
        TraktTokenGrant grant = new(
            "access-token-diagnostic-sentinel",
            "refresh-token-diagnostic-sentinel",
            TimeSpan.FromHours(1),
            DateTimeOffset.Parse("2026-07-14T10:00:00Z"));

        string diagnostic = grant.ToString();

        diagnostic.Should().Be("TraktTokenGrant { Values = [REDACTED] }");
        diagnostic.Should().NotContain("access-token-diagnostic-sentinel");
        diagnostic.Should().NotContain("refresh-token-diagnostic-sentinel");
    }

    [Fact]
    public void TraktDeviceStartDto_ToString_RedactsOneTimeUserCode()
    {
        TraktDeviceStartDto start = new(
            "one-time-user-code-diagnostic-sentinel",
            "https://trakt.tv/activate",
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
            5);

        string diagnostic = start.ToString();

        diagnostic.Should().Be("TraktDeviceStartDto { Values = [REDACTED] }");
        diagnostic.Should().NotContain("one-time-user-code-diagnostic-sentinel");
    }

    [Theory]
    [MemberData(nameof(PrivateOAuthDiagnostics))]
    public void PrivateOAuthRecord_ToString_RedactsEveryValue(
        string nestedTypeName,
        object?[] constructorArguments,
        string[] sentinels)
    {
        Type nestedType = typeof(TraktOAuthClient).GetNestedType(
            nestedTypeName,
            BindingFlags.NonPublic)!;
        object instance = Activator.CreateInstance(
            nestedType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: constructorArguments,
            culture: null)!;

        string diagnostic = instance.ToString()!;

        diagnostic.Should().Be($"{nestedTypeName} {{ Values = [REDACTED] }}");
        foreach (string sentinel in sentinels)
        {
            diagnostic.Should().NotContain(sentinel);
        }
    }

    public static TheoryData<string, object?[], string[]> PrivateOAuthDiagnostics => new()
    {
        {
            "DeviceCodeRequest",
            ["client-id-diagnostic-sentinel"],
            ["client-id-diagnostic-sentinel"]
        },
        {
            "DeviceTokenRequest",
            [
                "device-code-diagnostic-sentinel",
                "client-id-diagnostic-sentinel",
                "client-secret-diagnostic-sentinel"
            ],
            [
                "device-code-diagnostic-sentinel",
                "client-id-diagnostic-sentinel",
                "client-secret-diagnostic-sentinel"
            ]
        },
        {
            "RefreshTokenRequest",
            [
                "refresh-token-diagnostic-sentinel",
                "client-id-diagnostic-sentinel",
                "client-secret-diagnostic-sentinel",
                "redirect-uri-diagnostic-sentinel",
                "refresh_token"
            ],
            [
                "refresh-token-diagnostic-sentinel",
                "client-id-diagnostic-sentinel",
                "client-secret-diagnostic-sentinel",
                "redirect-uri-diagnostic-sentinel"
            ]
        },
        {
            "DeviceCodeResponse",
            [
                "device-code-diagnostic-sentinel",
                "user-code-diagnostic-sentinel",
                "verification-url-diagnostic-sentinel",
                600L,
                5L
            ],
            [
                "device-code-diagnostic-sentinel",
                "user-code-diagnostic-sentinel",
                "verification-url-diagnostic-sentinel"
            ]
        },
        {
            "TokenResponse",
            [
                "access-token-diagnostic-sentinel",
                "refresh-token-diagnostic-sentinel",
                3600L,
                1784023200L
            ],
            [
                "access-token-diagnostic-sentinel",
                "refresh-token-diagnostic-sentinel"
            ]
        }
    };
}
