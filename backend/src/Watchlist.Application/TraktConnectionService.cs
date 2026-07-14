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
    private static readonly TimeSpan CriticalSaveTimeout = TimeSpan.FromSeconds(10);
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
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            if (deviceCode.ExpiresIn <= TimeSpan.Zero)
            {
                throw new TraktParseException();
            }

            int pollIntervalSeconds = ValidatePollInterval(deviceCode.Interval);
            DateTimeOffset expiresAt = AddTimestamp(completedAt, deviceCode.ExpiresIn);
            DateTimeOffset nextPollAt = AddTimestamp(completedAt, deviceCode.Interval);
            string protectedDeviceCode = tokenProtector.Protect(deviceCode.DeviceCode);
            TraktConnection pending = new(
                "pending",
                protectedDeviceCode,
                null,
                deviceCode.VerificationUrl,
                expiresAt,
                deviceCode.Interval,
                nextPollAt,
                null,
                null,
                null,
                completedAt);
            await SaveCriticalAsync(pending);

            return new TraktDeviceStartDto(
                deviceCode.UserCode,
                deviceCode.VerificationUrl,
                expiresAt,
                pollIntervalSeconds);
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
                return await PersistUnreadablePendingAsync(connection, cancellationToken);
            }

            string deviceCode;
            try
            {
                deviceCode = tokenProtector.Unprotect(connection.ProtectedDeviceCode);
            }
            catch (TraktConnectionUnreadableException)
            {
                return await PersistUnreadablePendingAsync(connection, cancellationToken);
            }

            TraktTokenGrant? grant;
            try
            {
                grant = await oauthClient.PollDeviceAsync(deviceCode, cancellationToken);
            }
            catch (TraktDeviceAuthorizationException exception)
                when (exception.Code == "slow_down")
            {
                DateTimeOffset completedAt = timeProvider.GetUtcNow();
                TimeSpan interval = (connection.DevicePollInterval
                    ?? DefaultDevicePollInterval).Add(SlowDownIncrement);
                TraktConnection slowed = connection with
                {
                    DevicePollInterval = interval,
                    NextDevicePollAt = AddTimestamp(completedAt, interval),
                    UpdatedAt = completedAt
                };
                await SaveCriticalAsync(slowed);
                return new TraktConnectionStatusDto(
                    "pending",
                    null,
                    null,
                    "slow_down");
            }
            catch (TraktDeviceAuthorizationException exception)
                when (exception.Code is "denied" or "expired" or "invalid" or "already_used")
            {
                DateTimeOffset completedAt = timeProvider.GetUtcNow();
                TraktConnection revoked = ClearPendingState(
                    connection,
                    "revoked",
                    completedAt);
                await SaveCriticalAsync(revoked);
                return new TraktConnectionStatusDto(
                    "revoked",
                    null,
                    null,
                    exception.Code);
            }
            catch (TraktUnavailableException)
            {
                await PersistTransientBackoffAsync(
                    connection,
                    timeProvider.GetUtcNow());
                throw;
            }
            catch (TraktParseException)
            {
                await PersistTransientBackoffAsync(
                    connection,
                    timeProvider.GetUtcNow());
                throw;
            }

            DateTimeOffset responseCompletedAt = timeProvider.GetUtcNow();
            if (grant is null)
            {
                TimeSpan interval = connection.DevicePollInterval
                    ?? DefaultDevicePollInterval;
                TraktConnection scheduled = connection with
                {
                    DevicePollInterval = interval,
                    NextDevicePollAt = AddTimestamp(responseCompletedAt, interval),
                    UpdatedAt = responseCompletedAt
                };
                await SaveCriticalAsync(scheduled);
                return new TraktConnectionStatusDto("pending", null, null, null);
            }

            TraktConnection connected = CreateConnected(grant, responseCompletedAt);
            await SaveCriticalAsync(connected);
            return GetPublicStatus(connected);
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

            return await RefreshAsync(connection, cancellationToken);
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
            return await RefreshAsync(connection, cancellationToken);
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

    private async Task<TraktConnectionStatusDto> PersistUnreadablePendingAsync(
        TraktConnection connection,
        CancellationToken cancellationToken)
    {
        TraktConnection refreshRequired = connection with
        {
            State = "refresh_required",
            UpdatedAt = timeProvider.GetUtcNow()
        };
        await repository.SaveAsync(refreshRequired, cancellationToken);
        return UnreadableStatus();
    }

    private async Task PersistTransientBackoffAsync(
        TraktConnection connection,
        DateTimeOffset completedAt)
    {
        TimeSpan interval = connection.DevicePollInterval ?? DefaultDevicePollInterval;
        TraktConnection scheduled = connection with
        {
            DevicePollInterval = interval,
            NextDevicePollAt = AddTimestamp(completedAt, interval),
            UpdatedAt = completedAt
        };
        await SaveCriticalAsync(scheduled);
    }

    private async Task<string> RefreshAsync(
        TraktConnection connection,
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
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            TraktConnection refreshRequired = connection with
            {
                State = "refresh_required",
                UpdatedAt = completedAt
            };
            await SaveCriticalAsync(refreshRequired);
            throw new TraktNotConnectedException();
        }

        DateTimeOffset responseCompletedAt = timeProvider.GetUtcNow();
        TraktConnection refreshed = CreateConnected(grant, responseCompletedAt);
        await SaveCriticalAsync(refreshed);
        return grant.AccessToken;
    }

    private async Task SaveCriticalAsync(TraktConnection connection)
    {
        using CancellationTokenSource timeout = new(CriticalSaveTimeout, timeProvider);
        await repository.SaveAsync(connection, timeout.Token);
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
        if (grant.ExpiresIn <= TimeSpan.Zero)
        {
            throw new TraktParseException();
        }

        DateTimeOffset expiresAt = AddTimestamp(grant.CreatedAt, grant.ExpiresIn);
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
            expiresAt,
            now);
    }

    private static int ValidatePollInterval(TimeSpan interval)
    {
        long ticks = interval.Ticks;
        if (ticks <= 0 || ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new TraktParseException();
        }

        long seconds = ticks / TimeSpan.TicksPerSecond;
        if (seconds > int.MaxValue)
        {
            throw new TraktParseException();
        }

        return (int)seconds;
    }

    private static DateTimeOffset AddTimestamp(DateTimeOffset timestamp, TimeSpan duration)
    {
        try
        {
            return timestamp.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new TraktParseException();
        }
        catch (OverflowException)
        {
            throw new TraktParseException();
        }
    }

    private TraktConnectionStatusDto GetPublicStatus(TraktConnection connection)
    {
        if (connection.State is "connected" or "refresh_required")
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

            if (connection.State == "refresh_required")
            {
                return new TraktConnectionStatusDto(
                    "refresh_required",
                    null,
                    connection.AccessTokenExpiresAt,
                    "refresh_rejected");
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
