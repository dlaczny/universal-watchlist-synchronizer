namespace Watchlist.Application;

/// <summary>
/// Manages device authorization and protected OAuth token rotation for one Trakt account.
/// </summary>
public sealed class TraktConnectionService(
    ITraktConnectionRepository repository,
    ITraktOAuthClient oauthClient,
    ITraktTokenProtector tokenProtector,
    TimeProvider timeProvider,
    TimeSpan tokenRefreshSkew) : ITraktConnectionService, ITraktAccessTokenProvider
{
    private static readonly TimeSpan SlowDownIncrement = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultDevicePollInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<TraktDeviceStartDto> StartDeviceAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            TraktConnection? current = await repository.GetAsync(cancellationToken);
            if (current is not null
                && current.State == "pending"
                && current.DeviceCodeExpiresAt > now)
            {
                throw new TraktConnectionPendingException();
            }

            TraktDeviceCode deviceCode = await oauthClient.StartDeviceAsync(cancellationToken);
            string protectedDeviceCode = tokenProtector.Protect(deviceCode.DeviceCode);
            DateTimeOffset expiresAt = now.Add(deviceCode.ExpiresIn);
            TraktConnection pending = new(
                "pending",
                protectedDeviceCode,
                null,
                deviceCode.VerificationUrl,
                expiresAt,
                deviceCode.Interval,
                now.Add(deviceCode.Interval),
                null,
                null,
                null,
                now);
            await repository.SaveAsync(pending, cancellationToken);

            return new TraktDeviceStartDto(
                deviceCode.UserCode,
                deviceCode.VerificationUrl,
                expiresAt,
                checked((int)Math.Ceiling(deviceCode.Interval.TotalSeconds)));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TraktConnectionStatusDto> PollPendingAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TraktConnection? connection = await repository.GetAsync(cancellationToken);
            if (connection is null)
            {
                return DisconnectedStatus();
            }

            if (connection.State != "pending")
            {
                return GetPublicStatus(connection);
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (connection.DeviceCodeExpiresAt is null
                || connection.DeviceCodeExpiresAt <= now)
            {
                TraktConnection revoked = ClearPendingState(connection, "revoked", now);
                await repository.SaveAsync(revoked, cancellationToken);
                return new TraktConnectionStatusDto("revoked", null, null, "expired");
            }

            if (connection.NextDevicePollAt is not null
                && connection.NextDevicePollAt > now)
            {
                return new TraktConnectionStatusDto("pending", null, null, null);
            }

            if (string.IsNullOrWhiteSpace(connection.ProtectedDeviceCode))
            {
                return UnreadableStatus();
            }

            string deviceCode;
            try
            {
                deviceCode = tokenProtector.Unprotect(connection.ProtectedDeviceCode);
            }
            catch (TraktConnectionUnreadableException)
            {
                return UnreadableStatus();
            }

            try
            {
                TraktTokenGrant? grant = await oauthClient.PollDeviceAsync(
                    deviceCode,
                    cancellationToken);
                if (grant is null)
                {
                    TimeSpan interval = connection.DevicePollInterval
                        ?? DefaultDevicePollInterval;
                    TraktConnection scheduled = connection with
                    {
                        DevicePollInterval = interval,
                        NextDevicePollAt = now.Add(interval),
                        UpdatedAt = now
                    };
                    await repository.SaveAsync(scheduled, cancellationToken);
                    return new TraktConnectionStatusDto("pending", null, null, null);
                }

                TraktConnection connected = CreateConnected(grant, now);
                await repository.SaveAsync(connected, cancellationToken);
                return GetPublicStatus(connected);
            }
            catch (TraktDeviceAuthorizationException exception)
                when (exception.Code == "slow_down")
            {
                TimeSpan interval = (connection.DevicePollInterval
                    ?? DefaultDevicePollInterval).Add(SlowDownIncrement);
                TraktConnection slowed = connection with
                {
                    DevicePollInterval = interval,
                    NextDevicePollAt = now.Add(interval),
                    UpdatedAt = now
                };
                await repository.SaveAsync(slowed, cancellationToken);
                return new TraktConnectionStatusDto(
                    "pending",
                    null,
                    null,
                    "slow_down");
            }
            catch (TraktDeviceAuthorizationException exception)
                when (exception.Code is "denied" or "expired" or "invalid" or "already_used")
            {
                TraktConnection revoked = ClearPendingState(connection, "revoked", now);
                await repository.SaveAsync(revoked, cancellationToken);
                return new TraktConnectionStatusDto(
                    "revoked",
                    null,
                    null,
                    exception.Code);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TraktConnectionStatusDto> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TraktConnection? connection = await repository.GetAsync(cancellationToken);
            return connection is null
                ? DisconnectedStatus()
                : GetPublicStatus(connection);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TraktConnectionStatusDto> DisconnectAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await repository.DeleteAsync(cancellationToken);
            return DisconnectedStatus();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> GetValidAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TraktConnection connection = await GetConnectedAsync(cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (connection.AccessTokenExpiresAt is not null
                && connection.AccessTokenExpiresAt > now.Add(tokenRefreshSkew))
            {
                return UnprotectConnectedValue(connection.ProtectedAccessToken);
            }

            return await RefreshAsync(connection, now, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> ForceRefreshAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TraktConnection connection = await GetConnectedAsync(cancellationToken);
            return await RefreshAsync(
                connection,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TraktConnection> GetConnectedAsync(CancellationToken cancellationToken)
    {
        TraktConnection? connection = await repository.GetAsync(cancellationToken);
        if (connection is null || connection.State != "connected")
        {
            throw new TraktNotConnectedException();
        }

        return connection;
    }

    private async Task<string> RefreshAsync(
        TraktConnection connection,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string refreshToken = UnprotectConnectedValue(connection.ProtectedRefreshToken);
        TraktTokenGrant grant;
        try
        {
            grant = await oauthClient.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (TraktRefreshRejectedException)
        {
            TraktConnection refreshRequired = connection with
            {
                State = "refresh_required",
                UpdatedAt = now
            };
            await repository.SaveAsync(refreshRequired, cancellationToken);
            throw new TraktNotConnectedException();
        }

        TraktConnection refreshed = CreateConnected(grant, now);
        await repository.SaveAsync(refreshed, cancellationToken);
        return grant.AccessToken;
    }

    private string UnprotectConnectedValue(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            throw new TraktNotConnectedException();
        }

        try
        {
            return tokenProtector.Unprotect(ciphertext);
        }
        catch (TraktConnectionUnreadableException)
        {
            throw new TraktNotConnectedException();
        }
    }

    private TraktConnection CreateConnected(TraktTokenGrant grant, DateTimeOffset now)
    {
        string protectedAccessToken = tokenProtector.Protect(grant.AccessToken);
        string protectedRefreshToken = tokenProtector.Protect(grant.RefreshToken);
        return new TraktConnection(
            "connected",
            null,
            null,
            null,
            null,
            null,
            null,
            protectedAccessToken,
            protectedRefreshToken,
            grant.CreatedAt.Add(grant.ExpiresIn),
            now);
    }

    private TraktConnectionStatusDto GetPublicStatus(TraktConnection connection)
    {
        if (connection.State == "connected")
        {
            if (string.IsNullOrWhiteSpace(connection.ProtectedAccessToken)
                || string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
            {
                return UnreadableStatus();
            }

            try
            {
                tokenProtector.Unprotect(connection.ProtectedAccessToken);
                tokenProtector.Unprotect(connection.ProtectedRefreshToken);
            }
            catch (TraktConnectionUnreadableException)
            {
                return UnreadableStatus();
            }

            return new TraktConnectionStatusDto(
                "connected",
                connection.UpdatedAt,
                connection.AccessTokenExpiresAt,
                null);
        }

        if (connection.State == "pending")
        {
            if (string.IsNullOrWhiteSpace(connection.ProtectedDeviceCode))
            {
                return UnreadableStatus();
            }

            try
            {
                tokenProtector.Unprotect(connection.ProtectedDeviceCode);
            }
            catch (TraktConnectionUnreadableException)
            {
                return UnreadableStatus();
            }

            return new TraktConnectionStatusDto("pending", null, null, null);
        }

        return connection.State switch
        {
            "refresh_required" => new TraktConnectionStatusDto(
                "refresh_required",
                null,
                connection.AccessTokenExpiresAt,
                "refresh_rejected"),
            "revoked" => new TraktConnectionStatusDto("revoked", null, null, "revoked"),
            _ => DisconnectedStatus()
        };
    }

    private static TraktConnection ClearPendingState(
        TraktConnection connection,
        string state,
        DateTimeOffset now)
    {
        return connection with
        {
            State = state,
            ProtectedDeviceCode = null,
            UserCode = null,
            VerificationUrl = null,
            DeviceCodeExpiresAt = null,
            DevicePollInterval = null,
            NextDevicePollAt = null,
            UpdatedAt = now
        };
    }

    private static TraktConnectionStatusDto DisconnectedStatus()
    {
        return new TraktConnectionStatusDto("disconnected", null, null, null);
    }

    private static TraktConnectionStatusDto UnreadableStatus()
    {
        return new TraktConnectionStatusDto(
            "refresh_required",
            null,
            null,
            "token_unreadable");
    }
}
