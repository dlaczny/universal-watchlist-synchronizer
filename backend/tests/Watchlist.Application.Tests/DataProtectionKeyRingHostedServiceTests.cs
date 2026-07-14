using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class DataProtectionKeyRingHostedServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"watchlist-keyring-hosted-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task StartAsync_WithUsablePath_CreatesDirectoryAndProbesProtectorWithoutLeavingWriteProbe()
    {
        string keyRingPath = Path.Combine(tempDirectory, "created", "keys");
        RecordingTokenProtector protector = new();
        DataProtectionKeyRingHostedService service = CreateService(
            keyRingPath,
            Environments.Development,
            protector);

        await service.StartAsync(CancellationToken.None);

        Directory.Exists(keyRingPath).Should().BeTrue();
        Directory.GetFileSystemEntries(keyRingPath).Should().BeEmpty();
        protector.ProtectCallCount.Should().Be(1);
        protector.UnprotectCallCount.Should().Be(1);
        protector.LastPlaintext.Should().NotBeNull();
        Convert.FromBase64String(protector.LastPlaintext!).Should().HaveCount(32);
        protector.LastCiphertext.Should().Be(protector.LastUnprotectInput);
    }

    [Fact]
    public void AddWatchlistInfrastructure_ConfiguresPersistentPurposeScopedSingletonServices()
    {
        string keyRingPath = Path.Combine(tempDirectory, "registered-keys");
        Directory.CreateDirectory(keyRingPath);
        IConfiguration configuration = BuildInfrastructureConfiguration(
            keyRingPath,
            "registered-app");
        string plaintext = "registered-token-sentinel-08af53";
        string ciphertext;
        using (ServiceProvider provider = BuildInfrastructureProvider(configuration))
        {
            ITraktTokenProtector tokenProtector =
                provider.GetRequiredService<ITraktTokenProtector>();
            ITraktConnectionRepository repository =
                provider.GetRequiredService<ITraktConnectionRepository>();
            ITraktConnectionService connectionService =
                provider.GetRequiredService<ITraktConnectionService>();
            ITraktAccessTokenProvider accessTokenProvider =
                provider.GetRequiredService<ITraktAccessTokenProvider>();
            tokenProtector.Should().BeOfType<DataProtectionTraktTokenProtector>();
            tokenProtector.Should().BeSameAs(
                provider.GetRequiredService<ITraktTokenProtector>());
            repository.Should().BeOfType<MongoTraktConnectionRepository>();
            repository.Should().BeSameAs(
                provider.GetRequiredService<ITraktConnectionRepository>());
            connectionService.Should().BeOfType<TraktConnectionService>();
            accessTokenProvider.Should().BeSameAs(connectionService);
            connectionService.Should().BeSameAs(
                provider.GetRequiredService<ITraktConnectionService>());
            provider.GetRequiredService<ITraktOAuthClient>()
                .Should().BeOfType<TraktOAuthClient>();
            DataProtectionKeyRingOptions boundOptions = provider
                .GetRequiredService<IOptions<DataProtectionKeyRingOptions>>()
                .Value;
            boundOptions.KeyRingPath.Should().Be(keyRingPath);
            boundOptions.ApplicationName.Should().Be("registered-app");
            provider.GetServices<IHostedService>()
                .Should().Contain(service => service is DataProtectionKeyRingHostedService);
            provider.GetServices<IHostedService>()
                .Should().Contain(service => service is TraktDeviceAuthorizationHostedService);
            ciphertext = tokenProtector.Protect(plaintext);
        }

        using (ServiceProvider restartedProvider = BuildInfrastructureProvider(configuration))
        {
            ITraktTokenProtector restartedProtector =
                restartedProvider.GetRequiredService<ITraktTokenProtector>();
            restartedProtector.Unprotect(ciphertext).Should().Be(plaintext);
        }

        IConfiguration differentApplicationConfiguration = BuildInfrastructureConfiguration(
            keyRingPath,
            "different-app");
        using ServiceProvider differentApplicationProvider =
            BuildInfrastructureProvider(differentApplicationConfiguration);
        ITraktTokenProtector differentApplicationProtector =
            differentApplicationProvider.GetRequiredService<ITraktTokenProtector>();

        Action action = () => differentApplicationProtector.Unprotect(ciphertext);

        TraktConnectionUnreadableException exception = action.Should()
            .Throw<TraktConnectionUnreadableException>()
            .Which;
        exception.Message.Should().Be("The stored Trakt connection cannot be decrypted.");
        exception.Message.Should().NotContain(plaintext);
        exception.Message.Should().NotContain(ciphertext);
    }

    [Fact]
    public async Task StartAsync_InProductionWithRelativePath_RejectsConfigurationBeforeProbing()
    {
        RecordingTokenProtector protector = new();
        DataProtectionKeyRingHostedService service = CreateService(
            Path.Combine("relative", $"keys-{Guid.NewGuid():N}"),
            Environments.Production,
            protector);

        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>())
            .Which;
        exception.Message.Should().Be(
            "Data Protection key ring path must be absolute in Production.");
        protector.ProtectCallCount.Should().Be(0);
        protector.UnprotectCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WithUnusablePath_FailsCleanlyBeforeProbing()
    {
        Directory.CreateDirectory(tempDirectory);
        string filePath = Path.Combine(tempDirectory, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "occupied");
        RecordingTokenProtector protector = new();
        DataProtectionKeyRingHostedService service = CreateService(
            filePath,
            Environments.Development,
            protector);

        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>())
            .Which;
        exception.Message.Should().Be(
            "The Data Protection key ring path is unavailable or not writable.");
        protector.ProtectCallCount.Should().Be(0);
        protector.UnprotectCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WhenProtectionRoundTripDoesNotMatch_FailsWithoutExposingProbeValues()
    {
        string keyRingPath = Path.Combine(tempDirectory, "mismatch-keys");
        MismatchedTokenProtector protector = new();
        DataProtectionKeyRingHostedService service = CreateService(
            keyRingPath,
            Environments.Development,
            protector);

        Func<Task> action = () => service.StartAsync(CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>())
            .Which;
        exception.Message.Should().Be("The Data Protection startup probe failed.");
        exception.Message.Should().NotContain(protector.LastPlaintext!);
        exception.Message.Should().NotContain(protector.ProtectedPayload);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static DataProtectionKeyRingHostedService CreateService(
        string keyRingPath,
        string environmentName,
        ITraktTokenProtector protector)
    {
        DataProtectionKeyRingOptions options = new()
        {
            KeyRingPath = keyRingPath,
            ApplicationName = "watchlist-keyring-tests"
        };
        TestHostEnvironment environment = new()
        {
            EnvironmentName = environmentName
        };

        return new DataProtectionKeyRingHostedService(
            Options.Create(options),
            environment,
            protector);
    }

    private static ServiceProvider BuildInfrastructureProvider(IConfiguration configuration)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddWatchlistInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildInfrastructureConfiguration(
        string keyRingPath,
        string applicationName)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DataProtectionKeyRingOptions.SectionName}:KeyRingPath"] = keyRingPath,
                [$"{DataProtectionKeyRingOptions.SectionName}:ApplicationName"] = applicationName,
                [$"{MongoDbOptions.SectionName}:ConnectionString"] = "mongodb://localhost:27017",
                [$"{MongoDbOptions.SectionName}:DatabaseName"] = "registration-tests"
            })
            .Build();
    }

    private sealed class RecordingTokenProtector : ITraktTokenProtector
    {
        public int ProtectCallCount { get; private set; }

        public int UnprotectCallCount { get; private set; }

        public string? LastPlaintext { get; private set; }

        public string? LastCiphertext { get; private set; }

        public string? LastUnprotectInput { get; private set; }

        public string Protect(string plaintext)
        {
            ProtectCallCount++;
            LastPlaintext = plaintext;
            LastCiphertext = $"protected-{Guid.NewGuid():N}";
            return LastCiphertext;
        }

        public string Unprotect(string ciphertext)
        {
            UnprotectCallCount++;
            LastUnprotectInput = ciphertext;
            return LastPlaintext!;
        }
    }

    private sealed class MismatchedTokenProtector : ITraktTokenProtector
    {
        public string ProtectedPayload { get; } = "protected-probe-payload";

        public string? LastPlaintext { get; private set; }

        public string Protect(string plaintext)
        {
            LastPlaintext = plaintext;
            return ProtectedPayload;
        }

        public string Unprotect(string ciphertext)
        {
            return "a-different-probe-value";
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Watchlist.Application.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
