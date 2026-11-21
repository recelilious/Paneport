// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Steam.Session;
using Paneport.Steam.Tests.Fakes;
using Xunit;

namespace Paneport.Steam.Tests;

// S2.3.1: the connection reconnects after a drop, honors a sign-off request, logs on with the
// token after a foreground return, stops retrying at the documented limit, and moves to another
// server when Steam asks (steam-service.md section 4).
public sealed class SteamSessionTests : IDisposable
{
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);

    private readonly SteamTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task LogsOnWithTheSignInOverANewConnection()
    {
        var logOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), TestContext.Current.CancellationToken);

        Assert.Equal([FakeConnection.Connect], _kit.Connection.Requests);
        Assert.Equal(SteamSessionState.Connecting, _kit.Service.Status.State);
        _kit.Connection.Listener.OnConnected();
        Assert.Equal(SteamSessionState.LoggingOn, _kit.Service.Status.State);
        Assert.Equal((_kit.Samples.AccountName, _kit.Samples.RefreshToken), _kit.Connection.LastLogOn);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        Assert.Equal(SteamResult.OK, await logOn.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
        Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        Assert.Equal(SteamSignInState.SignedIn, _kit.Service.SignInState);
        Assert.Equal(_kit.Connection.Transport, _kit.Service.Status.Transport);
    }

    [Fact]
    public async Task ReconnectsAndLogsOnAgainAfterADrop()
    {
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);

        Assert.Equal(SteamSessionState.Reconnecting, _kit.Service.Status.State);
        Assert.Equal([s_second], _kit.Delays.Pending);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Connect));
        _kit.Delays.Release(s_second);
        Assert.Equal(2, _kit.Connection.Count(FakeConnection.Connect));
        _kit.Connection.Listener.OnConnected();
        Assert.Equal(2, _kit.Connection.Count(FakeConnection.LogOn));
        Assert.Equal((_kit.Samples.AccountName, _kit.Samples.RefreshToken), _kit.Connection.LastLogOn);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
    }

    [Fact]
    public async Task HonorsASignOffRequestWithoutConnectingAgain()
    {
        await _kit.LogOnAsync();

        var signOff = _kit.Session.SignOffAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SteamSessionState.SigningOut, _kit.Service.Status.State);
        Assert.Equal(FakeConnection.LogOff, _kit.Connection.Requests[^1]);
        _kit.Connection.Listener.OnLoggedOff(SteamResult.OK);
        Assert.Equal(FakeConnection.Disconnect, _kit.Connection.Requests[^1]);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOff.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Connect));
        Assert.DoesNotContain(_kit.Delays.Pending, delay => delay == s_second);
    }

    [Fact]
    public async Task SignOffDisconnectsWhenTheLogOffIsNotConfirmedInTime()
    {
        await _kit.LogOnAsync();

        var signOff = _kit.Session.SignOffAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeConnection.Disconnect, _kit.Connection.Requests);
        _kit.Delays.Release(ReconnectPolicy.Default.LogOffTimeout);
        Assert.Equal(FakeConnection.Disconnect, _kit.Connection.Requests[^1]);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);

        await signOff.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
    }

    [Fact]
    public async Task LogsOnWithTheTokenAfterAForegroundReturn()
    {
        await _kit.LogOnAsync();

        _kit.Service.NotifyForegroundReturned();
        Assert.Equal(FakeConnection.Disconnect, _kit.Connection.Requests[^1]);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        Assert.Equal(2, _kit.Connection.Count(FakeConnection.Connect));
        Assert.Equal(SteamSessionState.Reconnecting, _kit.Service.Status.State);
        _kit.Connection.Listener.OnConnected();
        Assert.Equal(2, _kit.Connection.Count(FakeConnection.LogOn));
        Assert.Equal((_kit.Samples.AccountName, _kit.Samples.RefreshToken), _kit.Connection.LastLogOn);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        Assert.Empty(_kit.Delays.Pending);
    }

    [Fact]
    public void AForegroundReturnDoesNothingWhileSignedOut()
    {
        _kit.Service.NotifyForegroundReturned();

        Assert.Empty(_kit.Connection.Requests);
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
    }

    [Fact]
    public async Task StopsRetryingAtTheDocumentedLimit()
    {
        var policy = ReconnectPolicy.Default;
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            var delay = policy.DelayBefore(attempt);
            Assert.Equal([delay], _kit.Delays.Pending);
            _kit.Delays.Release(delay);
            Assert.Equal(attempt + 1, _kit.Connection.Count(FakeConnection.Connect));
            _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        }

        Assert.Equal(SteamSessionState.Offline, _kit.Service.Status.State);
        Assert.Empty(_kit.Delays.Pending);
        Assert.Equal(policy.MaxAttempts + 1, _kit.Connection.Count(FakeConnection.Connect));
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)],
            _kit.Delays.Requested);
        Assert.NotNull(_kit.Service.Status.Problem);
    }

    [Fact]
    public async Task AForegroundReturnTriesAgainAfterGivingUp()
    {
        await _kit.LogOnAsync();
        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        for (var attempt = 1; attempt <= ReconnectPolicy.Default.MaxAttempts; attempt++)
        {
            _kit.Delays.Release(ReconnectPolicy.Default.DelayBefore(attempt));
            _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        }

        Assert.Equal(SteamSessionState.Offline, _kit.Service.Status.State);
        var connects = _kit.Connection.Count(FakeConnection.Connect);

        _kit.Service.NotifyForegroundReturned();
        Assert.Equal(connects + 1, _kit.Connection.Count(FakeConnection.Connect));
        _kit.Connection.Listener.OnConnected();
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
    }

    [Theory]
    [InlineData((int)SteamResult.TryAnotherCM)]
    [InlineData((int)SteamResult.ServiceUnavailable)]
    public async Task MovesToAnotherServerWhenSteamAsks(int result)
    {
        var logOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), TestContext.Current.CancellationToken);
        _kit.Connection.Listener.OnConnected();

        _kit.Connection.Listener.OnLoggedOn((SteamResult)result);
        Assert.Equal([FakeConnection.Connect, FakeConnection.LogOn, FakeConnection.AvoidServer, FakeConnection.Disconnect], _kit.Connection.Requests);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        Assert.Equal(2, _kit.Connection.Count(FakeConnection.Connect));
        _kit.Connection.Listener.OnConnected();
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        Assert.Equal(SteamResult.OK, await logOn.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
        Assert.Empty(_kit.Delays.Requested);
    }

    [Fact]
    public async Task StopsMovingServersAtTheLimit()
    {
        var logOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), TestContext.Current.CancellationToken);
        for (var move = 0; move < ReconnectPolicy.Default.MaxServerSwitches; move++)
        {
            _kit.Connection.Listener.OnConnected();
            _kit.Connection.Listener.OnLoggedOn(SteamResult.TryAnotherCM);
            _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        }

        _kit.Connection.Listener.OnConnected();
        _kit.Connection.Listener.OnLoggedOn(SteamResult.TryAnotherCM);

        Assert.Equal(SteamResult.TryAnotherCM, await logOn.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
        Assert.Equal(ReconnectPolicy.Default.MaxServerSwitches, _kit.Connection.Count(FakeConnection.AvoidServer));
    }

    [Fact]
    public async Task ALogOffBecauseOfAnotherLogonEndsTheSessionWithoutReconnecting()
    {
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnLoggedOff(SteamResult.LoggedInElsewhere);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);

        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Connect));
        Assert.Empty(_kit.Delays.Requested);
    }

    [Fact]
    public async Task AnotherLogOffLogsOnAgain()
    {
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnLoggedOff(SteamResult.ServiceUnavailable);
        Assert.Equal(FakeConnection.Disconnect, _kit.Connection.Requests[^1]);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        _kit.Connection.Listener.OnConnected();

        Assert.Equal(2, _kit.Connection.Count(FakeConnection.LogOn));
    }

    [Fact]
    public async Task TheLicenseListArrivesAfterTheLogon()
    {
        await _kit.LogOnAsync();

        var licenses = _kit.Session.GetLicensesAsync(TestContext.Current.CancellationToken);
        Assert.False(licenses.IsCompleted);
        _kit.Connection.Listener.OnLicenseList(SteamResult.OK, [11, 12, 13]);

        Assert.Equal([11u, 12u, 13u], await licenses.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheBackoffDoublesUpToItsCap()
    {
        var policy = ReconnectPolicy.Default;

        Assert.Equal(TimeSpan.FromSeconds(1), policy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(16), policy.DelayBefore(5));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.DelayBefore(6));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.DelayBefore(40));
    }
}
