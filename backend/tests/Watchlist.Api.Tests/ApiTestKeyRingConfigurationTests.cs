using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class ApiTestKeyRingConfigurationTests
{
    [Fact]
    public void SeededFactory_UsesTemporaryDataProtectionKeyRing()
    {
        using SeededApiFactory factory = new();

        DataProtectionKeyRingOptions options = factory.Services
            .GetRequiredService<IOptions<DataProtectionKeyRingOptions>>()
            .Value;

        options.KeyRingPath.Should().StartWith(Path.GetTempPath());
    }

    [Fact]
    public void MongoUnavailableFactory_UsesTemporaryDataProtectionKeyRing()
    {
        using MongoUnavailableApiFactory factory = new();

        DataProtectionKeyRingOptions options = factory.Services
            .GetRequiredService<IOptions<DataProtectionKeyRingOptions>>()
            .Value;

        options.KeyRingPath.Should().StartWith(Path.GetTempPath());
    }

    [Fact]
    public void SeededFactories_UseDistinctDataProtectionKeyRings()
    {
        using SeededApiFactory first = new();
        using SeededApiFactory second = new();

        string firstPath = first.Services
            .GetRequiredService<IOptions<DataProtectionKeyRingOptions>>()
            .Value
            .KeyRingPath;
        string secondPath = second.Services
            .GetRequiredService<IOptions<DataProtectionKeyRingOptions>>()
            .Value
            .KeyRingPath;

        firstPath.Should().NotBe(secondPath);
    }
}
