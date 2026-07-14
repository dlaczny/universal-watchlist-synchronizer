using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTraktConnectionRepositoryTests : IAsyncLifetime
{
    private const string CollectionName = "trakt_connections";
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly string keyRingPath = Path.Combine(
        Path.GetTempPath(),
        $"watchlist-trakt-repository-{Guid.NewGuid():N}");
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTraktConnectionRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            TraktConnectionsCollectionName = CollectionName
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task GetAsync_WhenConnectionDoesNotExist_ReturnsNull()
    {
        MongoTraktConnectionRepository repository = CreateRepository();

        TraktConnection? connection = await repository.GetAsync(CancellationToken.None);

        connection.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_PendingThenConnected_ReplacesSingletonAndClearsPendingSecrets()
    {
        IDataProtectionProvider provider = BuildProtectionProvider(keyRingPath);
        DataProtectionTraktTokenProtector protector = new(provider);
        MongoTraktConnectionRepository repository = CreateRepository();
        string deviceCode = "device-code-plaintext";
        string accessToken = "access-token-plaintext";
        string refreshToken = "refresh-token-plaintext";
        string protectedDeviceCode = protector.Protect(deviceCode);
        string protectedAccessToken = protector.Protect(accessToken);
        string protectedRefreshToken = protector.Protect(refreshToken);
        DateTimeOffset pendingUpdatedAt = DateTimeOffset.Parse("2026-07-14T10:00:00Z");
        TraktConnection pending = new(
            "pending",
            protectedDeviceCode,
            "USER-CODE",
            "https://trakt.tv/activate",
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
            TimeSpan.FromSeconds(7.5),
            DateTimeOffset.Parse("2026-07-14T10:00:07.5Z"),
            null,
            null,
            null,
            pendingUpdatedAt);

        await repository.SaveAsync(pending, CancellationToken.None);

        TraktConnection? pendingResult = await repository.GetAsync(CancellationToken.None);
        pendingResult.Should().Be(pending);
        BsonDocument pendingDocument = await GetRawDocumentAsync();
        pendingDocument["_id"].AsString.Should().Be("single-account");
        pendingDocument["protectedDeviceCode"].AsString.Should().Be(protectedDeviceCode);
        pendingDocument.ToJson().Should().NotContain(deviceCode);

        DateTimeOffset connectedUpdatedAt = DateTimeOffset.Parse("2026-07-14T10:01:00Z");
        TraktConnection connectedWithStalePendingValues = new(
            "connected",
            protectedDeviceCode,
            "STALE-USER-CODE",
            "https://trakt.tv/activate",
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
            TimeSpan.FromSeconds(7.5),
            DateTimeOffset.Parse("2026-07-14T10:00:07.5Z"),
            protectedAccessToken,
            protectedRefreshToken,
            DateTimeOffset.Parse("2026-10-14T10:01:00Z"),
            connectedUpdatedAt);

        await repository.SaveAsync(connectedWithStalePendingValues, CancellationToken.None);

        TraktConnection? connectedResult = await repository.GetAsync(CancellationToken.None);
        connectedResult.Should().NotBeNull();
        connectedResult!.ProtectedDeviceCode.Should().BeNull();
        connectedResult.UserCode.Should().BeNull();
        connectedResult.ProtectedAccessToken.Should().Be(protectedAccessToken);
        connectedResult.ProtectedAccessToken.Should().NotContain(accessToken);
        connectedResult.ProtectedRefreshToken.Should().Be(protectedRefreshToken);
        connectedResult.DevicePollInterval.Should().Be(TimeSpan.FromSeconds(7.5));

        IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(CollectionName);
        long storedCount = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        storedCount.Should().Be(1);
        BsonDocument connectedDocument = await collection
            .Find(FilterDefinition<BsonDocument>.Empty)
            .SingleAsync();
        connectedDocument.Names.Should().BeEquivalentTo(
            "_id",
            "state",
            "protectedDeviceCode",
            "userCode",
            "verificationUrl",
            "deviceCodeExpiresAt",
            "devicePollIntervalSeconds",
            "nextDevicePollAt",
            "protectedAccessToken",
            "protectedRefreshToken",
            "accessTokenExpiresAt",
            "updatedAt");
        connectedDocument["_id"].AsString.Should().Be("single-account");
        connectedDocument["protectedDeviceCode"].Should().Be(BsonNull.Value);
        connectedDocument["userCode"].Should().Be(BsonNull.Value);
        connectedDocument["protectedAccessToken"].AsString.Should().Be(protectedAccessToken);
        connectedDocument["protectedRefreshToken"].AsString.Should().Be(protectedRefreshToken);
        string rawJson = connectedDocument.ToJson();
        rawJson.Should().NotContain(deviceCode);
        rawJson.Should().NotContain(accessToken);
        rawJson.Should().NotContain(refreshToken);
    }

    [Fact]
    public async Task DeleteAsync_WhenConnectionExists_RemovesSingletonRecord()
    {
        MongoTraktConnectionRepository repository = CreateRepository();
        TraktConnection connection = new(
            "pending",
            "protected-device-code",
            "USER-CODE",
            "https://trakt.tv/activate",
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"),
            TimeSpan.FromSeconds(5),
            DateTimeOffset.Parse("2026-07-14T10:00:05Z"),
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-07-14T10:00:00Z"));
        await repository.SaveAsync(connection, CancellationToken.None);

        await repository.DeleteAsync(CancellationToken.None);

        TraktConnection? result = await repository.GetAsync(CancellationToken.None);
        result.Should().BeNull();
        IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(CollectionName);
        long storedCount = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        storedCount.Should().Be(0);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
        if (Directory.Exists(keyRingPath))
        {
            Directory.Delete(keyRingPath, recursive: true);
        }
    }

    private MongoTraktConnectionRepository CreateRepository()
    {
        return new MongoTraktConnectionRepository(database, Options.Create(options));
    }

    private async Task<BsonDocument> GetRawDocumentAsync()
    {
        IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(CollectionName);
        return await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
    }

    private static IDataProtectionProvider BuildProtectionProvider(string path)
    {
        ServiceCollection services = new();
        services.AddDataProtection()
            .SetApplicationName("watchlist-trakt-repository-tests")
            .PersistKeysToFileSystem(Directory.CreateDirectory(path));
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IDataProtectionProvider>();
    }
}
