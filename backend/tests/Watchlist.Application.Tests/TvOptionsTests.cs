using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TvOptionsTests
{
    [Fact]
    public void TraktOptions_Constructor_UsesExpectedDefaults()
    {
        TraktOptions options = new();

        TraktOptions.SectionName.Should().Be("Trakt");
        options.BaseUrl.Should().Be("https://api.trakt.tv");
        options.ClientId.Should().BeEmpty();
        options.ClientSecret.Should().BeEmpty();
        options.RedirectUri.Should().Be("urn:ietf:wg:oauth:2.0:oob");
        options.ActivityPollInterval.Should().Be(TimeSpan.FromMinutes(5));
        options.FullSyncInterval.Should().Be(TimeSpan.FromHours(1));
        options.MetadataRefreshInterval.Should().Be(TimeSpan.FromDays(1));
        options.TokenRefreshSkew.Should().Be(TimeSpan.FromMinutes(5));
        options.PageSize.Should().Be(100);
    }

    [Fact]
    public void DataProtectionKeyRingOptions_Constructor_UsesExpectedDefaults()
    {
        DataProtectionKeyRingOptions options = new();

        DataProtectionKeyRingOptions.SectionName.Should().Be("DataProtection");
        options.KeyRingPath.Should().Be(".artifacts/data-protection-keys");
        options.ApplicationName.Should().Be("watchlist-api");
    }

    [Fact]
    public void TmdbOptions_Constructor_UsesPolishProviderDefaults()
    {
        TmdbOptions options = new();

        options.ProviderRegion.Should().Be("PL");
        options.OwnedProviderIds.Should().Equal(119, 1899, 1773);
        options.ProviderCacheLifetime.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TmdbOptions_EnvironmentStyleConfiguration_BindsProviderIds()
    {
        Dictionary<string, string?> environmentValues = new()
        {
            ["Tmdb__OwnedProviderIds__0"] = "8",
            ["Tmdb__OwnedProviderIds__1"] = "337",
            ["Tmdb__OwnedProviderIds__2"] = "531"
        };
        Dictionary<string, string?> configurationValues = environmentValues.ToDictionary(
            pair => pair.Key.Replace("__", ConfigurationPath.KeyDelimiter),
            pair => pair.Value);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        TmdbOptions options = configuration
            .GetSection(TmdbOptions.SectionName)
            .Get<TmdbOptions>()!;

        options.OwnedProviderIds.Should().Equal(8, 337, 531);
    }
}
