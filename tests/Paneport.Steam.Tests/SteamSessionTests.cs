// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
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
    // A server that accepts each connection and drops it before the logon's answer: every drop
    // counts, because only a logon that Steam accepts resets the count.
    [Fact]
    public async Task AConnectionThatDropsBeforeEachLogonStillEndsAtTheLimit()
    {
        var policy = ReconnectPolicy.Default;
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            var delay = policy.DelayBefore(attempt);
            Assert.Equal([delay], _kit.Delays.Pending);
            _kit.Delays.Release(delay);
            _kit.Connection.Listener.OnConnected();
            _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        }

        Assert.Equal(SteamSessionState.Offline, _kit.Service.Status.State);
        Assert.Empty(_kit.Delays.Pending);
        Assert.Equal(policy.MaxAttempts + 1, _kit.Connection.Count(FakeConnection.LogOn));
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)],
            _kit.Delays.Requested);
    }

    // Steam logs the session off after every logon: each logoff waits as the policy says, and the
    // session stops at the limit instead of logging on again at once, forever.
    [Fact]
    public async Task ALogoffAfterEachLogonEndsAtTheLimit()
    {
        var policy = ReconnectPolicy.Default;
        await _kit.LogOnAsync();

        for (var logoff = 1; logoff <= policy.MaxAttempts; logoff++)
        {
            _kit.Connection.Listener.OnLoggedOff(SteamResult.ServiceUnavailable);
            Assert.Equal(logoff, _kit.Connection.Count(FakeConnection.Disconnect));
            _kit.Connection.Listener.OnDisconnected(userInitiated: true);
            var delay = policy.DelayBefore(logoff);
            Assert.Equal([delay], _kit.Delays.Pending);
            _kit.Delays.Release(delay);
            _kit.Connection.Listener.OnConnected();
            _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
            Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        }

        _kit.Connection.Listener.OnLoggedOff(SteamResult.ServiceUnavailable);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);

        Assert.Equal(SteamSessionState.Offline, _kit.Service.Status.State);
        Assert.Empty(_kit.Delays.Pending);
        Assert.Equal(policy.MaxAttempts + 1, _kit.Connection.Count(FakeConnection.LogOn));
        Assert.NotNull(_kit.Service.Status.Problem);
    }

    // Another session with the same login ID replaced this one; logging on again would only
    // replace that one in turn.
    [Fact]
    public async Task AReplacedSessionDoesNotLogOnAgain()
    {
        await _kit.LogOnAsync();

        _kit.Connection.Listener.OnLoggedOff(SteamResult.LogonSessionReplaced);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);

        Assert.Equal(1, _kit.Connection.Count(FakeConnection.LogOn));
        Assert.Empty(_kit.Delays.Pending);
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
        Assert.Contains("replaced", _kit.Service.Status.Problem, StringComparison.Ordinal);
    }

    // A sign-off while the adapter still chooses a server reaches no connection; the connection
    // that opens afterwards ends at once, so that the sign-off completes.
    [Fact]
    public async Task ASignOffWhileConnectingEndsTheConnectionThatOpensLater()
    {
        var logOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), TestContext.Current.CancellationToken);
        var signOff = _kit.Session.SignOffAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Disconnect));

        _kit.Connection.Listener.OnConnected();

        Assert.Equal(2, _kit.Connection.Count(FakeConnection.Disconnect));
        Assert.Equal(0, _kit.Connection.Count(FakeConnection.LogOn));
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOff.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
        Assert.Equal(SteamResult.Cancelled, await logOn.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
    }

    // A logon with another sign-in while the first one's logon is on its way connects again, so
    // that the first one's answer cannot answer the second.
    [Fact]
    public async Task ALogOnWithAnotherSignInDuringALogonConnectsAgain()
    {
        var token = TestContext.Current.CancellationToken;
        var second = new SavedSignIn(_kit.Samples.AccountName + "-second", new Secret(_kit.Samples.RefreshToken + "second"));
        var firstLogOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), token);
        _kit.Connection.Listener.OnConnected();
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.LogOn));

        var secondLogOn = _kit.Session.LogOnAsync(second, token);

        Assert.Equal(SteamResult.Cancelled, await firstLogOn.WaitAsync(SteamTestKit.Patience, token));
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Disconnect));
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        // The session runs its posts in order, so once a later post shows in the status, it has
        // seen the answer on the connection that it ends, and ignored it.
        _kit.Session.SetSignInSaved(true);
        await SteamTestKit.Until(() => _kit.Service.Status.SignInSaved == true, "the session to see the answer");
        Assert.NotEqual(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        Assert.False(secondLogOn.IsCompleted);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        _kit.Connection.Listener.OnConnected();
        Assert.Equal((second.AccountName, second.RefreshToken.Reveal()), _kit.Connection.LastLogOn);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SteamResult.OK, await secondLogOn.WaitAsync(SteamTestKit.Patience, token));
    }

    // A return to the foreground while the adapter still picks the first server: the logon still
    // completes on the connection that comes, and a later drop waits the backoff's first second.
    [Fact]
    public async Task AForegroundReturnWhileConnectingStillLogsOnAndKeepsTheBackoff()
    {
        var token = TestContext.Current.CancellationToken;
        var logOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), token);
        _kit.Session.OnForegroundReturned();
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SteamResult.OK, await logOn.WaitAsync(SteamTestKit.Patience, token));

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);

        await SteamTestKit.Until(() => _kit.Delays.Pending.Count == 1, "the wait to connect again");
        Assert.Equal(TimeSpan.FromSeconds(1), _kit.Delays.Pending[0]);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.Connect));
    }

    // A logon that Steam accepts after failures starts the count again, so a later drop waits the
    // backoff's first second.
    [Fact]
    public async Task AnAcceptedLogonAfterFailuresStartsTheBackoffAgain()
    {
        await _kit.LogOnAsync();
        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        await SteamTestKit.Until(() => _kit.Delays.Pending.Count == 1, "the first wait");
        _kit.Delays.Release(TimeSpan.FromSeconds(1));
        await _kit.UntilRequested(FakeConnection.Connect, 2);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 2);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        await SteamTestKit.Until(() => _kit.Service.Status.State == SteamSessionState.LoggedOn, "the logon after the failure");

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);

        await SteamTestKit.Until(() => _kit.Delays.Pending.Count == 1, "the wait after the second drop");
        Assert.Equal(TimeSpan.FromSeconds(1), _kit.Delays.Pending[0]);
    }

    // The same sign-in read from the store again is a new object: no second connection.
    [Fact]
    public async Task ALogOnWithTheSameSignInReadAgainWaitsForTheSameLogon()
    {
        var token = TestContext.Current.CancellationToken;
        var firstLogOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), token);
        _kit.Connection.Listener.OnConnected();

        var secondLogOn = _kit.Session.LogOnAsync(_kit.SampleSignIn(), token);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        Assert.Equal(SteamResult.OK, await firstLogOn.WaitAsync(SteamTestKit.Patience, token));
        Assert.Equal(SteamResult.OK, await secondLogOn.WaitAsync(SteamTestKit.Patience, token));
        Assert.Equal(0, _kit.Connection.Count(FakeConnection.Disconnect));
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.LogOn));
    }
}
