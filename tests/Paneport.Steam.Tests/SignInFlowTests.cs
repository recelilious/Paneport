// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Steam.Session;
using Paneport.Steam.SignIn;
using Paneport.Steam.Tests.Fakes;
using Xunit;

namespace Paneport.Steam.Tests;

// S2.3.2: each sign-in flow reaches Logged on; a wrong code asks again; a declined approval falls
// back to a code; a QR challenge change updates the code; the password is cleared after use
// (steam-service.md section 5).
public sealed class SignInFlowTests : IDisposable
{
    private readonly SteamTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task AnApprovalInTheSteamAppReachesLoggedOn()
    {
        var attempt = StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);

        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);
        _kit.Time.Advance(TimeSpan.FromSeconds(12));
        _kit.Authenticator.Approve();
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(12), result.WaitingForUser);
        Assert.Equal(TimeSpan.FromSeconds(12), result.Total);
        Assert.Equal(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        Assert.Equal(1, _kit.Authenticator.CredentialSessions);
    }

    [Theory]
    [InlineData(AccountGuard.AuthenticatorCode, SteamCodeKind.Authenticator)]
    [InlineData(AccountGuard.Email, SteamCodeKind.Email)]
    public async Task ACodeReachesLoggedOn(AccountGuard guard, SteamCodeKind kind)
    {
        _kit.Authenticator.Guard = guard;
        var attempt = StartPasswordSignIn(SignInConfirmation.EnterCode);

        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);
        Assert.Equal(kind, attempt.Progress.CodeKind);
        Assert.False(attempt.Progress.CodeWasWrong);
        attempt.SubmitCode(" " + _kit.Authenticator.Code + " ");
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
    }

    [Fact]
    public async Task ChoosingACodeSkipsTheApproval()
    {
        var attempt = StartPasswordSignIn(SignInConfirmation.EnterCode);

        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);

        Assert.False(_kit.Authenticator.WaitsForApproval);
        Assert.Equal(SteamCodeKind.Authenticator, attempt.Progress.CodeKind);
    }

    [Fact]
    public async Task AWrongCodeAsksAgain()
    {
        _kit.Authenticator.Guard = AccountGuard.AuthenticatorCode;
        var attempt = StartPasswordSignIn(SignInConfirmation.EnterCode);
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);

        attempt.SubmitCode("WRONG");
        await SteamTestKit.Until(() => attempt.Progress is { Stage: SignInStage.WaitingForCode, CodeWasWrong: true }, "the code to be asked for again");
        attempt.SubmitCode(_kit.Authenticator.Code);
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal(1, _kit.Authenticator.CredentialSessions);
    }

    [Fact]
    public async Task ADeclinedApprovalFallsBackToACode()
    {
        var attempt = StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);

        _kit.Authenticator.DeclineApproval(SteamResult.FileNotFound);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);
        Assert.Equal(SteamCodeKind.Authenticator, attempt.Progress.CodeKind);
        attempt.SubmitCode(_kit.Authenticator.Code);
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal(2, _kit.Authenticator.CredentialSessions);
        Assert.All(_kit.Authenticator.Passwords, password => Assert.Equal(_kit.Samples.Password, password));
    }

    [Fact]
    public async Task TheUserCanChooseACodeWhileWaitingForTheApproval()
    {
        var attempt = StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);

        attempt.UseCodeInstead();
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);
        attempt.SubmitCode(_kit.Authenticator.Code);
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal(2, _kit.Authenticator.CredentialSessions);
    }

    [Fact]
    public async Task AChangedQrChallengeUpdatesTheCode()
    {
        var attempt = _kit.Service.StartQrSignIn();
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.ShowingQrCode);
        var first = attempt.Progress;
        Assert.Equal(_kit.Authenticator.Qr.Challenge, first.QrChallenge);
        Assert.Equal(0, first.QrRevision);

        _kit.Authenticator.Qr.ChangeChallenge("https://example.invalid/q/second");

        Assert.Equal("https://example.invalid/q/second", attempt.Progress.QrChallenge);
        Assert.Equal(1, attempt.Progress.QrRevision);
        Assert.DoesNotContain("example.invalid", attempt.Progress.ToString(), StringComparison.Ordinal);
        _kit.Time.Advance(TimeSpan.FromSeconds(20));
        _kit.Authenticator.Qr.Scan();
        var result = await LogOnAsync(attempt);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(20), result.WaitingForUser);
    }

    [Fact]
    public async Task ThePasswordIsDroppedWhenTheSignInSucceeds()
    {
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = (SignInFlow)StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);
        Assert.True(attempt.HoldsPassword);

        await ConnectAsync(attempt);
        await LogOnAsync(attempt);

        Assert.False(attempt.HoldsPassword);
        Assert.Equal([_kit.Samples.Password], _kit.Authenticator.Passwords);
    }

    [Fact]
    public async Task ThePasswordIsDroppedWhenTheSignInIsCancelled()
    {
        var attempt = (SignInFlow)StartPasswordSignIn(SignInConfirmation.EnterCode);
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);

        attempt.Cancel();
        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Cancelled, result.Outcome);
        Assert.Equal(SignInStage.Cancelled, attempt.Progress.Stage);
        Assert.False(attempt.HoldsPassword);
        Assert.Null(_kit.Store.SignIn);
    }

    [Fact]
    public async Task AWrongPasswordFailsWithAClearProblemAndDropsThePassword()
    {
        _kit.Authenticator.RefuseStart = SteamResult.InvalidPassword;
        var attempt = (SignInFlow)StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);

        await ConnectAsync(attempt);
        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("The account name or the password is wrong.", result.Problem);
        Assert.Equal(result.Problem, attempt.Progress.Problem);
        Assert.False(attempt.HoldsPassword);
        Assert.Null(_kit.Store.SignIn);
    }

    [Fact]
    public async Task ACodeWhileNoneIsAskedForIsIgnored()
    {
        var attempt = StartPasswordSignIn(SignInConfirmation.ApproveInSteamApp);
        await ConnectAsync(attempt);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);

        attempt.SubmitCode(_kit.Authenticator.Code);

        Assert.Equal(SignInStage.WaitingForApproval, attempt.Progress.Stage);
    }

    [Fact]
    public async Task ANewSignInCancelsTheRunningOne()
    {
        var first = StartPasswordSignIn(SignInConfirmation.EnterCode);
        await ConnectAsync(first);
        await SteamTestKit.UntilStage(first, SignInStage.WaitingForCode);

        var second = _kit.Service.StartQrSignIn();

        Assert.Equal(SignInOutcome.Cancelled, (await first.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken)).Outcome);
        await SteamTestKit.UntilStage(second, SignInStage.ShowingQrCode);
        _kit.Authenticator.Qr.Scan();
        Assert.Equal(SignInOutcome.LoggedOn, (await LogOnAsync(second)).Outcome);
    }

    private ISignInAttempt StartPasswordSignIn(SignInConfirmation confirmation) =>
        _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, confirmation);

    // Plays Steam's part of the connection that the sign-in asks for.
    private async Task ConnectAsync(ISignInAttempt attempt)
    {
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        if (_kit.Connection.Count(FakeConnection.Connect) == 1 && _kit.Connection.Count(FakeConnection.LogOn) == 0)
        {
            _kit.Connection.Listener.OnConnected();
        }

        await SteamTestKit.Until(() => attempt.Progress.Stage != SignInStage.Connecting, "the connection");
    }

    // Plays Steam's part of the logon that a confirmed sign-in asks for, and waits for its end.
    private async Task<SignInResult> LogOnAsync(ISignInAttempt attempt)
    {
        var logOns = _kit.Connection.Count(FakeConnection.LogOn);
        await _kit.UntilRequested(FakeConnection.LogOn, logOns == 0 ? 1 : logOns);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        return await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
    }
    // iOS closes the socket while the user approves in the Steam mobile app on the same phone.
    // SteamKit2 then ends the wait, which the adapter reports as NoConnection, and the flow asks for
    // a code on a new connection.
    [Fact]
    public async Task AnApprovalWaitThatTheConnectionEndedFallsBackToACodeOnANewConnection()
    {
        var token = TestContext.Current.CancellationToken;
        var attempt = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        _kit.Authenticator.DeclineApproval(SteamResult.NoConnection);
        await SteamTestKit.Until(() => _kit.Delays.Pending.Count == 1, "the wait before connecting again");
        await Task.Delay(50, token);
        Assert.Equal(1, _kit.Authenticator.CredentialSessions);
        Assert.NotEqual(SignInStage.WaitingForCode, attempt.Progress.Stage);
        _kit.Delays.Release(TimeSpan.FromSeconds(1));
        await _kit.UntilRequested(FakeConnection.Connect, 2);
        _kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);
        attempt.SubmitCode(_kit.Authenticator.Code);
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        Assert.Equal(SignInOutcome.LoggedOn, (await attempt.Completion.WaitAsync(SteamTestKit.Patience, token)).Outcome);
        Assert.Equal(2, _kit.Authenticator.CredentialSessions);
    }

    // A QR sign-in whose connection ended says so in plain words, not with a type name.
    [Fact]
    public async Task AQrSignInWhoseConnectionEndedSaysSo()
    {
        var attempt = _kit.Service.StartQrSignIn();
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.ShowingQrCode);

        _kit.Authenticator.Qr.Fail(SteamResult.NoConnection);

        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("The connection to Steam ended during the sign-in. Start it again.", result.Problem);
    }

    // A logon that the policy gave up on is no refusal by Steam.
    [Fact]
    public async Task ALogonThatCouldNotReachSteamSaysSo()
    {
        var policy = ReconnectPolicy.Default;
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        for (var attemptNumber = 1; attemptNumber <= policy.MaxAttempts; attemptNumber++)
        {
            _kit.Delays.Release(policy.DelayBefore(attemptNumber));
            _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        }

        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("Paneport could not reach Steam to log on.", result.Problem);
    }

    // A logon that a sign-out cancels is no refusal by Steam either.
    [Fact]
    public async Task AResumeThatASignOutCancelsSaysSo()
    {
        var token = TestContext.Current.CancellationToken;
        _kit.Store.SignIn = _kit.SampleSignIn();
        var resume = _kit.Service.ResumeAsync(token);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);

        var signOut = _kit.Service.SignOutAsync(token);
        var result = await resume.WaitAsync(SteamTestKit.Patience, token);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOut.WaitAsync(SteamTestKit.Patience, token);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("The logon was cancelled.", result.Problem);
    }

    // A sign-in cancelled while it waits for its logon gives the logon up: when Steam accepts it
    // after all, the session logs off and never logs on with that token again.
    [Fact]
    public async Task ASignInCancelledWhileLoggingOnGivesUpItsLogon()
    {
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);

        attempt.Cancel();
        Assert.Equal(SignInOutcome.Cancelled, (await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken)).Outcome);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);

        await _kit.UntilRequested(FakeConnection.LogOff, 1);
        Assert.NotEqual(SteamSessionState.LoggedOn, _kit.Service.Status.State);
        Assert.Empty(_kit.Store.Writes);
        _kit.Connection.Listener.OnLoggedOff(SteamResult.OK);
        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        Assert.Empty(_kit.Delays.Pending);
        Assert.Equal(1, _kit.Connection.Count(FakeConnection.LogOn));
    }

    // The service disposes an attempt that another sign-in replaced once it has ended; a page that
    // still holds it may cancel it, which does nothing.
    [Fact]
    public async Task CancellingAnAttemptThatHasEndedDoesNothing()
    {
        var first = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        _kit.Service.StartQrSignIn();
        Assert.Equal(SignInOutcome.Cancelled, (await first.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken)).Outcome);
        ((SignInFlow)first).Dispose();

        var exception = Record.Exception(first.Cancel);

        Assert.Null(exception);
    }

    // The log filter holds the password only while its attempt runs.
    [Fact]
    public async Task ThePasswordLeavesTheLogFilterWhenTheAttemptEnds()
    {
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        Assert.True(_kit.Log.Holds(_kit.Samples.Password));
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.False(_kit.Log.Holds(_kit.Samples.Password));
        Assert.True(_kit.Log.Holds(_kit.Samples.AccountName));
    }
}
