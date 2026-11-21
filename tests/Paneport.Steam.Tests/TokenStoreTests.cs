// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Steam.Tests.Fakes;
using Xunit;

namespace Paneport.Steam.Tests;

// S2.3.3: the token is stored, reused, dropped on each invalid-token result and deleted at
// sign-out, and nothing else is kept (C-0007 section 3, steam-service.md section 5).
public sealed class TokenStoreTests : IDisposable
{
    private readonly SteamTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task ASignInStoresTheAccountNameAndTheTokenAndNothingElse()
    {
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = _kit.Service.StartPasswordSignIn(_kit.Samples.AccountName, _kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        var written = Assert.Single(_kit.Store.Writes);
        Assert.Equal(_kit.Samples.AccountName, written.AccountName);
        Assert.Equal(_kit.Samples.RefreshToken, written.RefreshToken.Reveal());
        Assert.Equal(["write"], _kit.Store.Calls);
        Assert.Equal(true, _kit.Service.Status.SignInSaved);
    }

    [Fact]
    public async Task ASignInThatSteamDoesNotLogOnStoresNothing()
    {
        _kit.Authenticator.Guard = AccountGuard.None;
        var attempt = _kit.Service.StartQrSignIn();
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.ShowingQrCode);
        _kit.Authenticator.Qr.Scan();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.AccessDenied);

        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Empty(_kit.Store.Writes);
    }

    [Fact]
    public async Task AResumeReusesTheSavedToken()
    {
        _kit.Store.SignIn = _kit.SampleSignIn();

        var resume = _kit.Service.ResumeAsync(TestContext.Current.CancellationToken);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        var result = await resume.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.LoggedOn, result.Outcome);
        Assert.Equal((_kit.Samples.AccountName, _kit.Samples.RefreshToken), _kit.Connection.LastLogOn);
        Assert.Equal(0, _kit.Authenticator.CredentialSessions);
        Assert.Equal(["read"], _kit.Store.Calls);
        Assert.Equal(true, _kit.Service.Status.SignInSaved);
    }

    [Fact]
    public async Task AResumeWithNothingSavedDoesNotConnect()
    {
        var result = await _kit.Service.ResumeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.NothingSaved, result.Outcome);
        Assert.Empty(_kit.Connection.Requests);
        await SteamTestKit.Until(() => _kit.Service.Status.SignInSaved == false, "the status to say that nothing is saved");
    }

    [Theory]
    [InlineData((int)SteamResult.InvalidPassword)]
    [InlineData((int)SteamResult.InvalidSignature)]
    [InlineData((int)SteamResult.AccessDenied)]
    [InlineData((int)SteamResult.Expired)]
    [InlineData((int)SteamResult.Revoked)]
    public async Task ATokenThatSteamRefusesIsDropped(int result)
    {
        _kit.Store.SignIn = _kit.SampleSignIn();

        var resume = _kit.Service.ResumeAsync(TestContext.Current.CancellationToken);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn((SteamResult)result);
        var outcome = await resume.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, outcome.Outcome);
        await SteamTestKit.Until(() => _kit.Store.SignIn is null, "the saved sign-in to be dropped");
        Assert.Equal(["read", "delete"], _kit.Store.Calls);
        Assert.Equal(false, _kit.Service.Status.SignInSaved);
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
    }

    [Theory]
    [InlineData((int)SteamResult.RateLimitExceeded)]
    [InlineData((int)SteamResult.Fail)]
    [InlineData((int)SteamResult.AccountDisabled)]
    public async Task ATokenIsKeptWhenTheLogonFailsForAnotherReason(int result)
    {
        _kit.Store.SignIn = _kit.SampleSignIn();

        var resume = _kit.Service.ResumeAsync(TestContext.Current.CancellationToken);
        await _kit.UntilRequested(FakeConnection.Connect, 1);
        _kit.Connection.Listener.OnConnected();
        await _kit.UntilRequested(FakeConnection.LogOn, 1);
        _kit.Connection.Listener.OnLoggedOn((SteamResult)result);
        await resume.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.NotNull(_kit.Store.SignIn);
        Assert.Equal(["read"], _kit.Store.Calls);
    }

    [Fact]
    public async Task ATokenRefusedOnAReconnectIsDroppedToo()
    {
        await _kit.LogOnAsync();
        _kit.Store.SignIn = _kit.SampleSignIn();

        _kit.Connection.Listener.OnDisconnected(userInitiated: false);
        _kit.Delays.Release(TimeSpan.FromSeconds(1));
        _kit.Connection.Listener.OnConnected();
        _kit.Connection.Listener.OnLoggedOn(SteamResult.Revoked);

        await SteamTestKit.Until(() => _kit.Store.SignIn is null, "the saved sign-in to be dropped");
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
    }

    [Fact]
    public async Task SignOutDeletesTheToken()
    {
        _kit.Store.SignIn = _kit.SampleSignIn();
        await _kit.LogOnAsync();

        var signOut = _kit.Service.SignOutAsync(TestContext.Current.CancellationToken);
        await SteamTestKit.Until(() => _kit.Store.SignIn is null, "the sign-in to be deleted");
        await _kit.UntilRequested(FakeConnection.LogOff, 1);
        _kit.Connection.Listener.OnLoggedOff(SteamResult.OK);
        _kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOut.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(["delete"], _kit.Store.Calls);
        Assert.Equal(false, _kit.Service.Status.SignInSaved);
        Assert.Equal(SteamSessionState.SignedOut, _kit.Service.Status.State);
        Assert.Equal(SteamSignInState.SignedOut, _kit.Service.SignInState);
    }
}
