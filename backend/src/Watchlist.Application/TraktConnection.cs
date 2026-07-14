namespace Watchlist.Application;

/// <summary>
/// Represents the persisted state of the single Trakt account connection.
/// Protected device and token properties contain ciphertext before persistence;
/// <see cref="UserCode"/> is transient plaintext used during device authorization.
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
    DateTimeOffset UpdatedAt)
{
    public override string ToString()
    {
        return $"TraktConnection {{ State = {State} }}";
    }
}
