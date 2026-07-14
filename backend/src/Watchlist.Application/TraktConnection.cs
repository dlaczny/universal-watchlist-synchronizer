namespace Watchlist.Application;

/// <summary>
/// Represents the persisted state of the single Trakt account connection.
/// Sensitive values in this record are protected before persistence.
/// </summary>
public sealed record TraktConnection(
    string State,
    string? ProtectedDeviceCode,
    string? UserCode,
    string? VerificationUrl,
    DateTimeOffset? DeviceCodeExpiresAt,
    TimeSpan? DevicePollInterval,
    DateTimeOffset? NextDevicePollAt,
    string? ProtectedAccessToken,
    string? ProtectedRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset UpdatedAt);
