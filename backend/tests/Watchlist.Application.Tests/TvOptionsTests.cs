using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Watchlist.Application;
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
    public void AddWatchlistInfrastructure_EnvironmentStyleConfiguration_ReplacesOwnedProviderIds()
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
        ServiceCollection services = new();
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        TmdbOptions options = provider.GetRequiredService<IOptions<TmdbOptions>>().Value;
        TmdbEnrichmentSettings settings = provider.GetRequiredService<TmdbEnrichmentSettings>();

        options.OwnedProviderIds.Should().Equal(8, 337, 531);
        settings.OwnedProviderIds.Should().Equal(8, 337, 531);
        settings.ProviderRegion.Should().Be("PL");
    }

    [Fact]
    public void TmdbOptions_OwnedProviderIdsAssignment_PreservesAllConfiguredIds()
    {
        TmdbOptions options = new()
        {
            OwnedProviderIds = [119, 1899, 1773, 8]
        };

        options.OwnedProviderIds.Should().Equal(119, 1899, 1773, 8);
    }

    [Fact]
    public void TmdbOptions_OwnedProviderIdsAssignment_SnapshotsCallerCollection()
    {
        List<int> providerIds = [8, 337, 531];
        TmdbOptions options = new()
        {
            OwnedProviderIds = providerIds
        };

        providerIds.Add(1899);

        options.OwnedProviderIds.Should().Equal(8, 337, 531);
    }

    [Fact]
    public void AddWatchlistInfrastructure_TvEnrichmentDependenciesUseSingletonSafeLifetimes()
    {
        ServiceCollection services = new();
        services.AddWatchlistInfrastructure(new ConfigurationBuilder().Build());

        services.Single(descriptor => descriptor.ServiceType == typeof(ITmdbTvMetadataClient))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Single(descriptor => descriptor.ServiceType == typeof(ITmdbTvEnrichmentService))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Single(descriptor => descriptor.ServiceType == typeof(ITmdbProviderCatalogRepository))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
