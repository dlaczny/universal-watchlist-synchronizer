using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class ApiTestKeyRingConfigurationTests
{
    [Fact]
    public void SeededFactory_DoesNotStartPersistentDataProtectionKeyRingService()
    {
        using SeededApiFactory factory = new();

        factory.Services.GetServices<IHostedService>()
            .Should().NotContain(service => service is DataProtectionKeyRingHostedService);
    }

    [Fact]
    public void MongoUnavailableFactory_DoesNotStartPersistentDataProtectionKeyRingService()
    {
        using MongoUnavailableApiFactory factory = new();

        factory.Services.GetServices<IHostedService>()
            .Should().NotContain(service => service is DataProtectionKeyRingHostedService);
    }
}
