using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class TraktConnectionServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-14T10:00:00Z");

    [Fact]
    public async Task StartDeviceAsync_WhenDisconnected_PersistsProtectedPendingStateAndReturnsUserCodeOnce()
    {
        FakeRepository repository = new();
        FakeOAuthClient oauthClient = new()
        {
            DeviceCode = new TraktDeviceCode(
                "plain-device-code",
                "ABCD1234",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(10),
                TimeSpan.FromSeconds(5))
        };
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        TraktDeviceStartDto result = await service.StartDeviceAsync(CancellationToken.None);

        result.Should().Be(new TraktDeviceStartDto(
            "ABCD1234",
            "https://trakt.tv/activate",
            Now.AddMinutes(10),
            5));
        repository.Stored.Should().NotBeNull();
        repository.Stored!.State.Should().Be("pending");
        repository.Stored.ProtectedDeviceCode.Should().Be("protected:plain-device-code");
        repository.Stored.ProtectedDeviceCode.Should().NotBe("plain-device-code");
        repository.Stored.UserCode.Should().BeNull("the user code is returned only by start");
        repository.Stored.DeviceCodeExpiresAt.Should().Be(Now.AddMinutes(10));
        repository.Stored.DevicePollInterval.Should().Be(TimeSpan.FromSeconds(5));
        repository.Stored.NextDevicePollAt.Should().Be(Now.AddSeconds(5));
        repository.Stored.ProtectedAccessToken.Should().BeNull();
        repository.Stored.ProtectedRefreshToken.Should().BeNull();
        protector.ProtectedPlaintexts.Should().Equal("plain-device-code");

        TraktConnectionStatusDto status = await service.GetStatusAsync(CancellationToken.None);
        status.Status.Should().Be("pending");
        status.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain("UserCode");
    }

    [Fact]
    public async Task StartDeviceAsync_WhenUnexpiredFlowIsPending_RejectsBeforeUpstreamCall()
    {
        FakeRepository repository = new(PendingConnection(expiresAt: Now.AddMinutes(1)));
        FakeOAuthClient oauthClient = new();
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        Func<Task> action = async () => await service.StartDeviceAsync(CancellationToken.None);

        TraktConnectionPendingException exception = (await action.Should()
            .ThrowAsync<TraktConnectionPendingException>())
            .Which;
        exception.Message.Should().Be("A Trakt device authorization is already pending.");
        oauthClient.StartCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartDeviceAsync_WhenPendingFlowExpired_StartsNewFlow()
    {
        FakeRepository repository = new(PendingConnection(expiresAt: Now));
        FakeOAuthClient oauthClient = new();
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        TraktDeviceStartDto result = await service.StartDeviceAsync(CancellationToken.None);

        result.UserCode.Should().Be("NEWCODE");
        oauthClient.StartCallCount.Should().Be(1);
        repository.Stored!.State.Should().Be("pending");
    }

    [Fact]
    public async Task StartDeviceAsync_WhenCallerCancelsAfterChallenge_PersistsWithIndependentBoundedToken()
    {
        using CancellationTokenSource callerCancellation = new();
        FakeRepository repository = new() { RejectCanceledSaveTokens = true };
        FakeOAuthClient oauthClient = new()
        {
            AfterStartResponse = callerCancellation.Cancel
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        TraktDeviceStartDto result = await service.StartDeviceAsync(callerCancellation.Token);

        result.UserCode.Should().Be("NEWCODE");
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        repository.Stored!.State.Should().Be("pending");
        AssertIndependentSaveToken(repository, callerCancellation.Token);
    }

    [Fact]
    public async Task StartDeviceAsync_UsesChallengeResponseCompletionTimeForPersistedTimestamps()
    {
        MutableTimeProvider timeProvider = new(Now);
        FakeRepository repository = new();
        FakeOAuthClient oauthClient = new()
        {
            AfterStartResponse = () => timeProvider.Advance(TimeSpan.FromMinutes(2))
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector(),
            timeProvider);

        TraktDeviceStartDto result = await service.StartDeviceAsync(CancellationToken.None);

        DateTimeOffset completedAt = Now.AddMinutes(2);
        result.ExpiresAt.Should().Be(completedAt.AddMinutes(10));
        repository.Stored!.UpdatedAt.Should().Be(completedAt);
        repository.Stored.DeviceCodeExpiresAt.Should().Be(completedAt.AddMinutes(10));
        repository.Stored.NextDevicePollAt.Should().Be(completedAt.AddSeconds(5));
    }

    [Theory]
    [InlineData("expiry_overflow")]
    [InlineData("next_poll_overflow")]
    [InlineData("poll_seconds_overflow")]
    [InlineData("fractional_poll_seconds")]
    [InlineData("fractional_poll_seconds_near_limit")]
    [InlineData("zero_poll_seconds")]
    [InlineData("zero_expiry")]
    public async Task StartDeviceAsync_WhenChallengeRangesInvalid_ThrowsParseBeforeProtectionOrPersistence(
        string invalidKind)
    {
        TraktDeviceCode deviceCode = invalidKind switch
        {
            "expiry_overflow" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.MaxValue,
                TimeSpan.FromSeconds(5)),
            "next_poll_overflow" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(1),
                TimeSpan.MaxValue),
            "poll_seconds_overflow" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(1),
                TimeSpan.FromDays(30_000)),
            "fractional_poll_seconds" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMilliseconds(5_500)),
            "fractional_poll_seconds_near_limit" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(1),
                TimeSpan.FromSeconds(int.MaxValue).Add(TimeSpan.FromTicks(1))),
            "zero_poll_seconds" => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.FromMinutes(1),
                TimeSpan.Zero),
            _ => new TraktDeviceCode(
                "device-code",
                "USERCODE",
                "https://trakt.tv/activate",
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5))
        };
        FakeRepository repository = new();
        FakeOAuthClient oauthClient = new() { DeviceCode = deviceCode };
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        Func<Task> action = async () => await service.StartDeviceAsync(CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        exception.Message.Should().Be("The Trakt response could not be parsed.");
        repository.SaveCallCount.Should().Be(0);
        repository.Stored.Should().BeNull();
        protector.ProtectedPlaintexts.Should().BeEmpty();
    }

    [Fact]
    public async Task PollPendingAsync_WhenGrantSucceeds_ProtectsTokensAndClearsEveryPendingField()
    {
        FakeRepository repository = new(PendingConnection(nextPollAt: Now));
        FakeOAuthClient oauthClient = new()
        {
            PollResult = new TraktTokenGrant(
                "plain-access-token",
                "plain-refresh-token",
                TimeSpan.FromHours(2),
                Now.AddSeconds(-10))
        };
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("connected");
        result.ConnectedAt.Should().Be(Now);
        result.AccessTokenExpiresAt.Should().Be(Now.AddSeconds(-10).AddHours(2));
        repository.Stored.Should().NotBeNull();
        repository.Stored!.State.Should().Be("connected");
        repository.Stored.ProtectedDeviceCode.Should().BeNull();
        repository.Stored.UserCode.Should().BeNull();
        repository.Stored.VerificationUrl.Should().BeNull();
        repository.Stored.DeviceCodeExpiresAt.Should().BeNull();
        repository.Stored.DevicePollInterval.Should().BeNull();
        repository.Stored.NextDevicePollAt.Should().BeNull();
        repository.Stored.ProtectedAccessToken.Should().Be("protected:plain-access-token");
        repository.Stored.ProtectedRefreshToken.Should().Be("protected:plain-refresh-token");
        repository.Stored.AccessTokenExpiresAt.Should().Be(Now.AddSeconds(-10).AddHours(2));
        oauthClient.PolledDeviceCodes.Should().Equal("plain-device-code");
    }

    [Fact]
    public async Task PollPendingAsync_WhenCallerCancelsAfterGrant_PersistsTokensWithIndependentBoundedToken()
    {
        using CancellationTokenSource callerCancellation = new();
        FakeRepository repository = new(PendingConnection(nextPollAt: Now))
        {
            RejectCanceledSaveTokens = true
        };
        FakeOAuthClient oauthClient = new()
        {
            PollResult = new TraktTokenGrant(
                "post-cancel-access-token",
                "post-cancel-refresh-token",
                TimeSpan.FromHours(1),
                Now),
            AfterPollResponse = callerCancellation.Cancel
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(callerCancellation.Token);

        result.Status.Should().Be("connected");
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        repository.Stored!.ProtectedAccessToken.Should().Be("protected:post-cancel-access-token");
        repository.Stored.ProtectedRefreshToken.Should().Be("protected:post-cancel-refresh-token");
        AssertIndependentSaveToken(repository, callerCancellation.Token);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("slow_down")]
    [InlineData("transient")]
    [InlineData("terminal")]
    [InlineData("connected")]
    public async Task PollPendingAsync_UsesResponseCompletionTimeForEveryUpstreamOutcome(
        string outcome)
    {
        MutableTimeProvider timeProvider = new(Now);
        FakeRepository repository = new(PendingConnection(
            nextPollAt: Now,
            interval: TimeSpan.FromSeconds(7)));
        FakeOAuthClient oauthClient = new()
        {
            AfterPollResponse = () => timeProvider.Advance(TimeSpan.FromSeconds(30))
        };
        if (outcome == "slow_down")
        {
            oauthClient.PollException = new TraktDeviceAuthorizationException("slow_down");
        }
        else if (outcome == "transient")
        {
            oauthClient.PollException = new TraktUnavailableException();
        }
        else if (outcome == "terminal")
        {
            oauthClient.PollException = new TraktDeviceAuthorizationException("denied");
        }
        else if (outcome == "connected")
        {
            oauthClient.PollResult = new TraktTokenGrant(
                "completed-access-token",
                "completed-refresh-token",
                TimeSpan.FromHours(1),
                Now);
        }

        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector(),
            timeProvider);

        if (outcome == "transient")
        {
            Func<Task> action = async () => await service.PollPendingAsync(
                CancellationToken.None);
            await action.Should().ThrowAsync<TraktUnavailableException>();
        }
        else
        {
            await service.PollPendingAsync(CancellationToken.None);
        }

        DateTimeOffset completedAt = Now.AddSeconds(30);
        repository.Stored!.UpdatedAt.Should().Be(completedAt);
        if (outcome is "pending" or "transient")
        {
            repository.Stored.NextDevicePollAt.Should().Be(completedAt.AddSeconds(7));
        }
        else if (outcome == "slow_down")
        {
            repository.Stored.NextDevicePollAt.Should().Be(completedAt.AddSeconds(12));
        }
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("refresh")]
    public async Task TokenGrant_WhenExpiryTimestampOverflows_ThrowsParseBeforeProtectionOrPersistence(
        string operation)
    {
        TraktConnection initial = operation == "poll"
            ? PendingConnection(nextPollAt: Now)
            : ConnectedConnection(Now.AddMinutes(1));
        FakeRepository repository = new(initial);
        TraktTokenGrant invalidGrant = new(
            "overflow-access-token",
            "overflow-refresh-token",
            TimeSpan.FromSeconds(1),
            DateTimeOffset.MaxValue);
        FakeOAuthClient oauthClient = new();
        if (operation == "poll")
        {
            oauthClient.PollResult = invalidGrant;
        }
        else
        {
            oauthClient.RefreshResult = invalidGrant;
        }

        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        async Task InvokeAsync()
        {
            if (operation == "poll")
            {
                await service.PollPendingAsync(CancellationToken.None);
                return;
            }

            await service.GetValidAccessTokenAsync(CancellationToken.None);
        }

        Func<Task> action = InvokeAsync;

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        exception.Message.Should().Be("The Trakt response could not be parsed.");
        repository.Stored.Should().BeSameAs(initial);
        repository.SaveCallCount.Should().Be(0);
        protector.ProtectedPlaintexts.Should().BeEmpty();
    }

    [Fact]
    public async Task PollPendingAsync_WhenAuthorizationPending_SchedulesFromCurrentTimeUsingPersistedInterval()
    {
        FakeRepository repository = new(PendingConnection(
            nextPollAt: Now,
            interval: TimeSpan.FromSeconds(7)));
        FakeOAuthClient oauthClient = new() { PollResult = null };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("pending");
        repository.Stored!.DevicePollInterval.Should().Be(TimeSpan.FromSeconds(7));
        repository.Stored.NextDevicePollAt.Should().Be(Now.AddSeconds(7));
        repository.Stored.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task PollPendingAsync_WhenSlowDown_IncreasesIntervalExactlyFiveSecondsBeforeScheduling()
    {
        FakeRepository repository = new(PendingConnection(
            nextPollAt: Now,
            interval: TimeSpan.FromSeconds(7)));
        FakeOAuthClient oauthClient = new()
        {
            PollException = new TraktDeviceAuthorizationException("slow_down")
        };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("pending");
        result.LastErrorCode.Should().Be("slow_down");
        repository.Stored!.DevicePollInterval.Should().Be(TimeSpan.FromSeconds(12));
        repository.Stored.NextDevicePollAt.Should().Be(Now.AddSeconds(12));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("parse")]
    public async Task PollPendingAsync_WhenTransientFailureOccurs_PersistsDurableBackoffBeforeRethrowing(
        string failureKind)
    {
        Exception failure = failureKind == "unavailable"
            ? new TraktUnavailableException()
            : new TraktParseException();
        FakeRepository repository = new(PendingConnection(
            nextPollAt: Now,
            interval: TimeSpan.FromSeconds(7)));
        FakeOAuthClient oauthClient = new() { PollException = failure };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        Func<Task> action = async () => await service.PollPendingAsync(CancellationToken.None);

        Exception thrown = (await action.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().BeSameAs(failure);
        repository.Stored!.State.Should().Be("pending");
        repository.Stored.DevicePollInterval.Should().Be(TimeSpan.FromSeconds(7));
        repository.Stored.NextDevicePollAt.Should().Be(Now.AddSeconds(7));
        repository.Stored.UpdatedAt.Should().Be(Now);
        repository.SaveCallCount.Should().Be(1);

        await service.PollPendingAsync(CancellationToken.None);

        oauthClient.PollCallCount.Should().Be(1);
        repository.SaveCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unreadable-ciphertext")]
    public async Task PollPendingAsync_WhenPendingDeviceCiphertextIsNotUsable_PersistsNonPollableStateWithoutDeletingCiphertext(
        string? protectedDeviceCode)
    {
        TraktConnection pending = PendingConnection(nextPollAt: Now) with
        {
            ProtectedDeviceCode = protectedDeviceCode
        };
        FakeRepository repository = new(pending);
        FakeOAuthClient oauthClient = new();
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("refresh_required");
        result.LastErrorCode.Should().Be("token_unreadable");
        repository.Stored!.State.Should().Be("refresh_required");
        repository.Stored.ProtectedDeviceCode.Should().Be(protectedDeviceCode);
        repository.Stored.ProtectedAccessToken.Should().BeNull();
        repository.Stored.ProtectedRefreshToken.Should().BeNull();
        repository.SaveCallCount.Should().Be(1);
        repository.DeleteCallCount.Should().Be(0);
        oauthClient.PollCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartDeviceAsync_AfterPendingCiphertextBecomesUnreadable_ReplacesNonPollableState()
    {
        FakeRepository repository = new(PendingConnection(nextPollAt: Now) with
        {
            ProtectedDeviceCode = "unreadable-ciphertext"
        });
        FakeOAuthClient oauthClient = new();
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());
        await service.PollPendingAsync(CancellationToken.None);

        TraktDeviceStartDto result = await service.StartDeviceAsync(CancellationToken.None);

        result.UserCode.Should().Be("NEWCODE");
        repository.Stored!.State.Should().Be("pending");
        repository.Stored.ProtectedDeviceCode.Should().Be("protected:new-device-code");
        oauthClient.StartCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("expired")]
    [InlineData("invalid")]
    [InlineData("already_used")]
    public async Task PollPendingAsync_WhenAuthorizationTerminates_RevokesAndClearsPendingFields(
        string code)
    {
        FakeRepository repository = new(PendingConnection(nextPollAt: Now));
        FakeOAuthClient oauthClient = new()
        {
            PollException = new TraktDeviceAuthorizationException(code)
        };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("revoked");
        result.LastErrorCode.Should().Be(code);
        AssertPendingFieldsCleared(repository.Stored!);
        repository.Stored!.State.Should().Be("revoked");
        repository.Stored.ProtectedAccessToken.Should().BeNull();
    }

    [Fact]
    public async Task PollPendingAsync_BeforeNextTimestamp_DoesNotDecryptOrCallUpstream()
    {
        FakeRepository repository = new(PendingConnection(nextPollAt: Now.AddSeconds(1)));
        FakeOAuthClient oauthClient = new();
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("pending");
        oauthClient.PollCallCount.Should().Be(0);
        protector.UnprotectedCiphertexts.Should().BeEmpty();
        repository.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PollPendingAsync_AtExpiry_RevokesLocallyWithoutUpstreamCall()
    {
        FakeRepository repository = new(PendingConnection(
            expiresAt: Now,
            nextPollAt: Now.AddMinutes(1)));
        FakeOAuthClient oauthClient = new();
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        TraktConnectionStatusDto result = await service.PollPendingAsync(CancellationToken.None);

        result.Status.Should().Be("revoked");
        result.LastErrorCode.Should().Be("expired");
        oauthClient.PollCallCount.Should().Be(0);
        AssertPendingFieldsCleared(repository.Stored!);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_OutsideRefreshSkew_ReturnsExistingTokenWithoutRefresh()
    {
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(6)));
        FakeOAuthClient oauthClient = new();
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        string result = await service.GetValidAccessTokenAsync(CancellationToken.None);

        result.Should().Be("plain-access-token");
        oauthClient.RefreshCallCount.Should().Be(0);
        repository.SaveCallCount.Should().Be(0);
        protector.UnprotectedCiphertexts.Should().Equal("protected:plain-access-token");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetValidAccessTokenAsync_AtOrInsideRefreshSkew_RotatesBothTokens(int minutesToExpiry)
    {
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(minutesToExpiry)));
        FakeOAuthClient oauthClient = new()
        {
            RefreshResult = new TraktTokenGrant(
                "rotated-access-token",
                "rotated-refresh-token",
                TimeSpan.FromHours(3),
                Now.AddSeconds(-30))
        };
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        string result = await service.GetValidAccessTokenAsync(CancellationToken.None);

        result.Should().Be("rotated-access-token");
        oauthClient.RefreshedTokens.Should().Equal("plain-refresh-token");
        repository.Stored!.State.Should().Be("connected");
        repository.Stored.ProtectedAccessToken.Should().Be("protected:rotated-access-token");
        repository.Stored.ProtectedRefreshToken.Should().Be("protected:rotated-refresh-token");
        repository.Stored.AccessTokenExpiresAt.Should().Be(Now.AddSeconds(-30).AddHours(3));
    }

    [Fact]
    public async Task ForceRefreshAsync_WhenTokenIsFresh_RefreshesRegardlessOfExpiry()
    {
        FakeRepository repository = new(ConnectedConnection(Now.AddDays(1)));
        FakeOAuthClient oauthClient = new()
        {
            RefreshResult = new TraktTokenGrant(
                "forced-access-token",
                "forced-refresh-token",
                TimeSpan.FromHours(1),
                Now)
        };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        string result = await service.ForceRefreshAsync(CancellationToken.None);

        result.Should().Be("forced-access-token");
        oauthClient.RefreshCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenCallerCancelsAfterRefreshGrant_PersistsRotatedTokens()
    {
        using CancellationTokenSource callerCancellation = new();
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(1)))
        {
            RejectCanceledSaveTokens = true
        };
        FakeOAuthClient oauthClient = new()
        {
            RefreshResult = new TraktTokenGrant(
                "post-cancel-rotated-access",
                "post-cancel-rotated-refresh",
                TimeSpan.FromHours(1),
                Now),
            AfterRefreshResponse = callerCancellation.Cancel
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        string result = await service.GetValidAccessTokenAsync(callerCancellation.Token);

        result.Should().Be("post-cancel-rotated-access");
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        repository.Stored!.ProtectedAccessToken.Should().Be("protected:post-cancel-rotated-access");
        repository.Stored.ProtectedRefreshToken.Should().Be("protected:post-cancel-rotated-refresh");
        AssertIndependentSaveToken(repository, callerCancellation.Token);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_UsesRefreshGrantCompletionTimeForUpdatedAt()
    {
        MutableTimeProvider timeProvider = new(Now);
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(1)));
        FakeOAuthClient oauthClient = new()
        {
            AfterRefreshResponse = () => timeProvider.Advance(TimeSpan.FromSeconds(30))
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector(),
            timeProvider);

        await service.GetValidAccessTokenAsync(CancellationToken.None);

        repository.Stored!.UpdatedAt.Should().Be(Now.AddSeconds(30));
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenRefreshDefinitelyRejected_PersistsRefreshRequired()
    {
        TraktConnection original = ConnectedConnection(Now.AddMinutes(1));
        FakeRepository repository = new(original);
        FakeOAuthClient oauthClient = new()
        {
            RefreshException = new TraktRefreshRejectedException()
        };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        Func<Task<string>> action = () => service.GetValidAccessTokenAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TraktNotConnectedException>();
        repository.Stored!.State.Should().Be("refresh_required");
        repository.Stored.ProtectedAccessToken.Should().Be(original.ProtectedAccessToken);
        repository.Stored.ProtectedRefreshToken.Should().Be(original.ProtectedRefreshToken);
        TraktConnectionStatusDto status = await service.GetStatusAsync(CancellationToken.None);
        status.Status.Should().Be("refresh_required");
        status.LastErrorCode.Should().Be("refresh_rejected");
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenCallerCancelsAfterDefiniteRejection_PersistsRefreshRequired()
    {
        using CancellationTokenSource callerCancellation = new();
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(1)))
        {
            RejectCanceledSaveTokens = true
        };
        FakeOAuthClient oauthClient = new()
        {
            RefreshException = new TraktRefreshRejectedException(),
            AfterRefreshResponse = callerCancellation.Cancel
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector());

        Func<Task<string>> action = () => service.GetValidAccessTokenAsync(
            callerCancellation.Token);

        await action.Should().ThrowAsync<TraktNotConnectedException>();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        repository.Stored!.State.Should().Be("refresh_required");
        AssertIndependentSaveToken(repository, callerCancellation.Token);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_UsesRefreshRejectionCompletionTimeForUpdatedAt()
    {
        MutableTimeProvider timeProvider = new(Now);
        FakeRepository repository = new(ConnectedConnection(Now.AddMinutes(1)));
        FakeOAuthClient oauthClient = new()
        {
            RefreshException = new TraktRefreshRejectedException(),
            AfterRefreshResponse = () => timeProvider.Advance(TimeSpan.FromSeconds(30))
        };
        TraktConnectionService service = CreateService(
            repository,
            oauthClient,
            new TrackingProtector(),
            timeProvider);

        Func<Task<string>> action = () => service.GetValidAccessTokenAsync(
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktNotConnectedException>();
        repository.Stored!.UpdatedAt.Should().Be(Now.AddSeconds(30));
    }

    [Theory]
    [MemberData(nameof(TransientRefreshFailures))]
    public async Task GetValidAccessTokenAsync_WhenRefreshFailsTransiently_PreservesConnectedRecord(
        Exception exception)
    {
        TraktConnection original = ConnectedConnection(Now.AddMinutes(1));
        FakeRepository repository = new(original);
        FakeOAuthClient oauthClient = new() { RefreshException = exception };
        TraktConnectionService service = CreateService(repository, oauthClient, new TrackingProtector());

        Func<Task<string>> action = () => service.GetValidAccessTokenAsync(CancellationToken.None);

        await action.Should().ThrowAsync<Exception>();
        repository.Stored.Should().BeSameAs(original);
        repository.SaveCallCount.Should().Be(0);
    }

    public static TheoryData<Exception> TransientRefreshFailures => new()
    {
        new TraktUnavailableException(),
        new TraktParseException()
    };

    [Theory]
    [InlineData("pending")]
    [InlineData("revoked")]
    [InlineData("refresh_required")]
    [InlineData("disconnected")]
    public async Task AccessTokenMethods_WhenPersistedStateIsNotConnected_ThrowWithoutDecryptOrOauth(
        string state)
    {
        FakeRepository repository = new(ConnectedConnection(Now.AddHours(1)) with { State = state });
        FakeOAuthClient oauthClient = new();
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, oauthClient, protector);

        Func<Task<string>> getAction = () => service.GetValidAccessTokenAsync(CancellationToken.None);
        Func<Task<string>> forceAction = () => service.ForceRefreshAsync(CancellationToken.None);

        await getAction.Should().ThrowAsync<TraktNotConnectedException>();
        await forceAction.Should().ThrowAsync<TraktNotConnectedException>();
        protector.UnprotectedCiphertexts.Should().BeEmpty();
        oauthClient.RefreshCallCount.Should().Be(0);
    }

    [Fact]
    public async Task AccessTokenMethods_WhenNoRecordExists_ThrowNotConnected()
    {
        FakeRepository repository = new();
        TraktConnectionService service = CreateService(
            repository,
            new FakeOAuthClient(),
            new TrackingProtector());

        Func<Task<string>> getAction = () => service.GetValidAccessTokenAsync(CancellationToken.None);
        Func<Task<string>> forceAction = () => service.ForceRefreshAsync(CancellationToken.None);

        await getAction.Should().ThrowAsync<TraktNotConnectedException>();
        await forceAction.Should().ThrowAsync<TraktNotConnectedException>();
    }

    [Fact]
    public async Task DisconnectAsync_DeletesSingletonAndReturnsDisconnected()
    {
        FakeRepository repository = new(ConnectedConnection(Now.AddHours(1)));
        TraktConnectionService service = CreateService(
            repository,
            new FakeOAuthClient(),
            new TrackingProtector());

        TraktConnectionStatusDto result = await service.DisconnectAsync(CancellationToken.None);

        result.Should().Be(new TraktConnectionStatusDto("disconnected", null, null, null));
        repository.Stored.Should().BeNull();
        repository.DeleteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_WhenCiphertextUnreadable_ReturnsRefreshRequiredWithoutOverwritingStoredFields()
    {
        TraktConnection unreadable = ConnectedConnection(Now.AddHours(1)) with
        {
            ProtectedAccessToken = "unreadable-ciphertext",
            ProtectedRefreshToken = "unreadable-ciphertext"
        };
        FakeRepository repository = new(unreadable);
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(repository, new FakeOAuthClient(), protector);

        TraktConnectionStatusDto status = await service.GetStatusAsync(CancellationToken.None);
        Func<Task<string>> getAction = () => service.GetValidAccessTokenAsync(CancellationToken.None);
        Func<Task<string>> forceAction = () => service.ForceRefreshAsync(CancellationToken.None);

        status.Status.Should().Be("refresh_required");
        status.LastErrorCode.Should().Be("token_unreadable");
        await getAction.Should().ThrowAsync<TraktNotConnectedException>();
        await forceAction.Should().ThrowAsync<TraktNotConnectedException>();
        repository.SaveCallCount.Should().Be(0);
        repository.DeleteCallCount.Should().Be(0);
        repository.Stored.Should().BeSameAs(unreadable);
        repository.Stored!.ProtectedAccessToken.Should().Be("unreadable-ciphertext");
        repository.Stored.ProtectedRefreshToken.Should().Be("unreadable-ciphertext");
    }

    [Fact]
    public async Task GetStatusAsync_WhenRefreshRequiredCiphertextBecomesUnreadable_ReportsTokenUnreadableWithoutMutation()
    {
        TraktConnection refreshRequired = ConnectedConnection(Now.AddMinutes(1)) with
        {
            State = "refresh_required",
            ProtectedAccessToken = "unreadable-ciphertext",
            ProtectedRefreshToken = "unreadable-ciphertext"
        };
        FakeRepository repository = new(refreshRequired);
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(
            repository,
            new FakeOAuthClient(),
            protector);

        TraktConnectionStatusDto status = await service.GetStatusAsync(CancellationToken.None);

        status.Status.Should().Be("refresh_required");
        status.LastErrorCode.Should().Be("token_unreadable");
        protector.UnprotectedCiphertexts.Should().Contain("unreadable-ciphertext");
        repository.SaveCallCount.Should().Be(0);
        repository.DeleteCallCount.Should().Be(0);
        repository.Stored.Should().BeSameAs(refreshRequired);
        repository.Stored!.ProtectedAccessToken.Should().Be("unreadable-ciphertext");
        repository.Stored.ProtectedRefreshToken.Should().Be("unreadable-ciphertext");
    }

    [Fact]
    public async Task GetStatusAsync_WhenRefreshRequiredCiphertextIsReadable_ReportsRefreshRejectedWithoutMutation()
    {
        TraktConnection refreshRequired = ConnectedConnection(Now.AddMinutes(1)) with
        {
            State = "refresh_required"
        };
        FakeRepository repository = new(refreshRequired);
        TrackingProtector protector = new();
        TraktConnectionService service = CreateService(
            repository,
            new FakeOAuthClient(),
            protector);

        TraktConnectionStatusDto status = await service.GetStatusAsync(CancellationToken.None);

        status.Status.Should().Be("refresh_required");
        status.LastErrorCode.Should().Be("refresh_rejected");
        protector.UnprotectedCiphertexts.Should().Equal(
            "protected:plain-access-token",
            "protected:plain-refresh-token");
        repository.SaveCallCount.Should().Be(0);
        repository.DeleteCallCount.Should().Be(0);
        repository.Stored.Should().BeSameAs(refreshRequired);
    }

    [Fact]
    public async Task GetStatusAsync_WhenConnected_UsesUpdatedAtAsConnectedAt()
    {
        TraktConnection connection = ConnectedConnection(Now.AddHours(1)) with
        {
            UpdatedAt = Now.AddMinutes(-3)
        };
        TraktConnectionService service = CreateService(
            new FakeRepository(connection),
            new FakeOAuthClient(),
            new TrackingProtector());

        TraktConnectionStatusDto result = await service.GetStatusAsync(CancellationToken.None);

        result.Status.Should().Be("connected");
        result.ConnectedAt.Should().Be(Now.AddMinutes(-3));
        result.AccessTokenExpiresAt.Should().Be(Now.AddHours(1));
        result.LastErrorCode.Should().BeNull();
    }

    private static TraktConnectionService CreateService(
        FakeRepository repository,
        FakeOAuthClient oauthClient,
        TrackingProtector protector,
        MutableTimeProvider? timeProvider = null)
    {
        return new TraktConnectionService(
            repository,
            oauthClient,
            protector,
            timeProvider ?? new MutableTimeProvider(Now),
            TimeSpan.FromMinutes(5));
    }

    private static TraktConnection PendingConnection(
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? nextPollAt = null,
        TimeSpan? interval = null)
    {
        return new TraktConnection(
            "pending",
            "protected:plain-device-code",
            null,
            "https://trakt.tv/activate",
            expiresAt ?? Now.AddMinutes(10),
            interval ?? TimeSpan.FromSeconds(5),
            nextPollAt ?? Now,
            null,
            null,
            null,
            Now.AddMinutes(-1));
    }

    private static TraktConnection ConnectedConnection(DateTimeOffset accessTokenExpiresAt)
    {
        return new TraktConnection(
            "connected",
            null,
            null,
            null,
            null,
            null,
            null,
            "protected:plain-access-token",
            "protected:plain-refresh-token",
            accessTokenExpiresAt,
            Now.AddMinutes(-1));
    }

    private static void AssertPendingFieldsCleared(TraktConnection connection)
    {
        connection.ProtectedDeviceCode.Should().BeNull();
        connection.UserCode.Should().BeNull();
        connection.VerificationUrl.Should().BeNull();
        connection.DeviceCodeExpiresAt.Should().BeNull();
        connection.DevicePollInterval.Should().BeNull();
        connection.NextDevicePollAt.Should().BeNull();
    }

    private static void AssertIndependentSaveToken(
        FakeRepository repository,
        CancellationToken callerToken)
    {
        repository.SaveCancellationTokens.Should().ContainSingle();
        CancellationToken saveToken = repository.SaveCancellationTokens.Single();
        saveToken.Should().NotBe(callerToken);
        saveToken.CanBeCanceled.Should().BeTrue();
        saveToken.IsCancellationRequested.Should().BeFalse();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }

    private sealed class FakeRepository(TraktConnection? connection = null)
        : ITraktConnectionRepository
    {
        public TraktConnection? Stored { get; private set; } = connection;

        public int SaveCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public bool RejectCanceledSaveTokens { get; init; }

        public List<CancellationToken> SaveCancellationTokens { get; } = [];

        public Task<TraktConnection?> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Stored);
        }

        public Task SaveAsync(TraktConnection savedConnection, CancellationToken cancellationToken)
        {
            SaveCancellationTokens.Add(cancellationToken);
            if (RejectCanceledSaveTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            SaveCallCount++;
            Stored = savedConnection;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            Stored = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOAuthClient : ITraktOAuthClient
    {
        public TraktDeviceCode DeviceCode { get; set; } = new(
            "new-device-code",
            "NEWCODE",
            "https://trakt.tv/activate",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(5));

        public TraktTokenGrant? PollResult { get; set; }

        public Exception? PollException { get; set; }

        public TraktTokenGrant RefreshResult { get; set; } = new(
            "new-access-token",
            "new-refresh-token",
            TimeSpan.FromHours(1),
            Now);

        public Exception? RefreshException { get; set; }

        public Action? AfterStartResponse { get; init; }

        public Action? AfterPollResponse { get; init; }

        public Action? AfterRefreshResponse { get; init; }

        public int StartCallCount { get; private set; }

        public int PollCallCount { get; private set; }

        public int RefreshCallCount { get; private set; }

        public List<string> PolledDeviceCodes { get; } = [];

        public List<string> RefreshedTokens { get; } = [];

        public Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken)
        {
            StartCallCount++;
            AfterStartResponse?.Invoke();
            return Task.FromResult(DeviceCode);
        }

        public Task<TraktTokenGrant?> PollDeviceAsync(
            string deviceCode,
            CancellationToken cancellationToken)
        {
            PollCallCount++;
            PolledDeviceCodes.Add(deviceCode);
            AfterPollResponse?.Invoke();
            if (PollException is not null)
            {
                return Task.FromException<TraktTokenGrant?>(PollException);
            }

            return Task.FromResult(PollResult);
        }

        public Task<TraktTokenGrant> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            RefreshCallCount++;
            RefreshedTokens.Add(refreshToken);
            AfterRefreshResponse?.Invoke();
            if (RefreshException is not null)
            {
                return Task.FromException<TraktTokenGrant>(RefreshException);
            }

            return Task.FromResult(RefreshResult);
        }
    }

    private sealed class TrackingProtector : ITraktTokenProtector
    {
        public List<string> ProtectedPlaintexts { get; } = [];

        public List<string> UnprotectedCiphertexts { get; } = [];

        public string Protect(string plaintext)
        {
            ProtectedPlaintexts.Add(plaintext);
            return $"protected:{plaintext}";
        }

        public string Unprotect(string ciphertext)
        {
            UnprotectedCiphertexts.Add(ciphertext);
            if (ciphertext == "unreadable-ciphertext")
            {
                throw new TraktConnectionUnreadableException();
            }

            return ciphertext.StartsWith("protected:", StringComparison.Ordinal)
                ? ciphertext["protected:".Length..]
                : ciphertext;
        }
    }
}
