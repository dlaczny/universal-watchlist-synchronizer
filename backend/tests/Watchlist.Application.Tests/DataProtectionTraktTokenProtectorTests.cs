using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class DataProtectionTraktTokenProtectorTests : IDisposable
{
    private const string ApplicationName = "watchlist-trakt-tests";
    private const string ProtectorPurpose = "Watchlist.Trakt.SingleAccountTokens.v1";
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"watchlist-trakt-protection-{Guid.NewGuid():N}");

    [Fact]
    public void ProtectAndUnprotect_WithPersistedKeyRing_SurvivesProviderRestart()
    {
        string keyRingPath = Path.Combine(tempDirectory, "restart-keys");
        string ciphertext;
        using (ServiceProvider firstServices = BuildServiceProvider(keyRingPath))
        {
            IDataProtectionProvider firstProvider =
                firstServices.GetRequiredService<IDataProtectionProvider>();
            DataProtectionTraktTokenProtector firstProtector = new(firstProvider);
            ciphertext = firstProtector.Protect("access-token");
        }

        using ServiceProvider restartedServices = BuildServiceProvider(keyRingPath);
        IDataProtectionProvider restartedProvider =
            restartedServices.GetRequiredService<IDataProtectionProvider>();
        DataProtectionTraktTokenProtector restartedProtector = new(restartedProvider);

        restartedProtector.Unprotect(ciphertext).Should().Be("access-token");
        ciphertext.Should().NotContain("access-token");
    }

    [Fact]
    public void ProtectAndUnprotect_UsesTheSingleAccountPurposeExactly()
    {
        using ServiceProvider services = BuildServiceProvider(
            Path.Combine(tempDirectory, "purpose-keys"));
        IDataProtectionProvider provider = services.GetRequiredService<IDataProtectionProvider>();
        DataProtectionTraktTokenProtector traktProtector = new(provider);
        IDataProtector exactPurposeProtector = provider.CreateProtector(ProtectorPurpose);

        string wrapperCiphertext = traktProtector.Protect("wrapper-token");
        string exactPurposeCiphertext = exactPurposeProtector.Protect("purpose-token");

        exactPurposeProtector.Unprotect(wrapperCiphertext).Should().Be("wrapper-token");
        traktProtector.Unprotect(exactPurposeCiphertext).Should().Be("purpose-token");
    }

    [Fact]
    public void Unprotect_WithDifferentKeyRing_ThrowsSanitizedUnreadableConnectionException()
    {
        using ServiceProvider firstServices = BuildServiceProvider(
            Path.Combine(tempDirectory, "first-keys"));
        IDataProtectionProvider firstProvider =
            firstServices.GetRequiredService<IDataProtectionProvider>();
        DataProtectionTraktTokenProtector firstProtector = new(firstProvider);
        string plaintext = "access-token-that-must-not-leak";
        string ciphertext = firstProtector.Protect(plaintext);
        using ServiceProvider unrelatedServices = BuildServiceProvider(
            Path.Combine(tempDirectory, "second-keys"));
        IDataProtectionProvider unrelatedProvider =
            unrelatedServices.GetRequiredService<IDataProtectionProvider>();
        DataProtectionTraktTokenProtector unrelatedProtector = new(unrelatedProvider);

        Action action = () => unrelatedProtector.Unprotect(ciphertext);

        TraktConnectionUnreadableException exception = action.Should()
            .Throw<TraktConnectionUnreadableException>()
            .Which;
        exception.Message.Should().Be("The stored Trakt connection cannot be decrypted.");
        exception.Message.Should().NotContain(plaintext);
        exception.Message.Should().NotContain(ciphertext);
    }

    [Fact]
    public void TraktConnectionStatusDto_ExposesOnlyPublicConnectionStatusFields()
    {
        IReadOnlyDictionary<string, Type> expectedProperties = new Dictionary<string, Type>
        {
            ["Status"] = typeof(string),
            ["ConnectedAt"] = typeof(DateTimeOffset?),
            ["AccessTokenExpiresAt"] = typeof(DateTimeOffset?),
            ["LastErrorCode"] = typeof(string)
        };

        Dictionary<string, Type> actualProperties = typeof(TraktConnectionStatusDto)
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.PropertyType);

        actualProperties.Should().BeEquivalentTo(expectedProperties);
    }

    [Fact]
    public void TraktConnectionUnreadableException_HasOnlyFixedSanitizedDiagnostics()
    {
        Type exceptionType = typeof(TraktConnectionUnreadableException);

        exceptionType.GetConstructors().Should().ContainSingle(constructor =>
            constructor.GetParameters().Length == 0);
        TraktConnectionUnreadableException exception =
            (TraktConnectionUnreadableException)Activator.CreateInstance(exceptionType)!;
        exception.Message.Should().Be("The stored Trakt connection cannot be decrypted.");
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain("inner-secret-diagnostic-sentinel");
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static ServiceProvider BuildServiceProvider(string keyRingPath)
    {
        DirectoryInfo keyRing = Directory.CreateDirectory(keyRingPath);
        ServiceCollection services = new();
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(keyRing);
        return services.BuildServiceProvider();
    }
}
