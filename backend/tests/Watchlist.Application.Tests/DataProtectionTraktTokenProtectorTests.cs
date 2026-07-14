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
        IDataProtectionProvider firstProvider = BuildProvider(keyRingPath);
        DataProtectionTraktTokenProtector firstProtector = new(firstProvider);

        string ciphertext = firstProtector.Protect("access-token");
        IDataProtectionProvider restartedProvider = BuildProvider(keyRingPath);
        DataProtectionTraktTokenProtector restartedProtector = new(restartedProvider);

        restartedProtector.Unprotect(ciphertext).Should().Be("access-token");
        ciphertext.Should().NotContain("access-token");
    }

    [Fact]
    public void ProtectAndUnprotect_UsesTheSingleAccountPurposeExactly()
    {
        IDataProtectionProvider provider = BuildProvider(Path.Combine(tempDirectory, "purpose-keys"));
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
        DataProtectionTraktTokenProtector firstProtector = new(BuildProvider(
            Path.Combine(tempDirectory, "first-keys")));
        string plaintext = "access-token-that-must-not-leak";
        string ciphertext = firstProtector.Protect(plaintext);
        DataProtectionTraktTokenProtector unrelatedProtector = new(BuildProvider(
            Path.Combine(tempDirectory, "second-keys")));

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

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static IDataProtectionProvider BuildProvider(string keyRingPath)
    {
        DirectoryInfo keyRing = Directory.CreateDirectory(keyRingPath);
        ServiceCollection services = new();
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(keyRing);
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IDataProtectionProvider>();
    }
}
