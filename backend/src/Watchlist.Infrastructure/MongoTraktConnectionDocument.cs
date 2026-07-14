using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTraktConnectionDocument
{
    public const string SingletonId = "single-account";

    [BsonId]
    public string Id { get; init; } = SingletonId;

    [BsonElement("state")]
    public string State { get; init; } = string.Empty;

    [BsonElement("protectedDeviceCode")]
    public string? ProtectedDeviceCode { get; init; }

    [BsonElement("userCode")]
    public string? UserCode { get; init; }

    [BsonElement("verificationUrl")]
    public string? VerificationUrl { get; init; }

    [BsonElement("deviceCodeExpiresAt")]
    public DateTimeOffset? DeviceCodeExpiresAt { get; init; }

    [BsonElement("devicePollIntervalSeconds")]
    public double? DevicePollIntervalSeconds { get; init; }

    [BsonElement("nextDevicePollAt")]
    public DateTimeOffset? NextDevicePollAt { get; init; }

    [BsonElement("protectedAccessToken")]
    public string? ProtectedAccessToken { get; init; }

    [BsonElement("protectedRefreshToken")]
    public string? ProtectedRefreshToken { get; init; }

    [BsonElement("accessTokenExpiresAt")]
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    [BsonElement("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    public static MongoTraktConnectionDocument FromDomain(TraktConnection connection)
    {
        bool hasAccessToken = connection.ProtectedAccessToken is not null;
        return new MongoTraktConnectionDocument
        {
            State = connection.State,
            ProtectedDeviceCode = hasAccessToken ? null : connection.ProtectedDeviceCode,
            UserCode = hasAccessToken ? null : connection.UserCode,
            VerificationUrl = connection.VerificationUrl,
            DeviceCodeExpiresAt = connection.DeviceCodeExpiresAt,
            DevicePollIntervalSeconds = connection.DevicePollInterval?.TotalSeconds,
            NextDevicePollAt = connection.NextDevicePollAt,
            ProtectedAccessToken = connection.ProtectedAccessToken,
            ProtectedRefreshToken = connection.ProtectedRefreshToken,
            AccessTokenExpiresAt = connection.AccessTokenExpiresAt,
            UpdatedAt = connection.UpdatedAt
        };
    }

    public TraktConnection ToDomain()
    {
        bool hasAccessToken = ProtectedAccessToken is not null;
        return new TraktConnection(
            State,
            hasAccessToken ? null : ProtectedDeviceCode,
            hasAccessToken ? null : UserCode,
            VerificationUrl,
            DeviceCodeExpiresAt,
            DevicePollIntervalSeconds is null
                ? null
                : TimeSpan.FromSeconds(DevicePollIntervalSeconds.Value),
            NextDevicePollAt,
            ProtectedAccessToken,
            ProtectedRefreshToken,
            AccessTokenExpiresAt,
            UpdatedAt);
    }
}
