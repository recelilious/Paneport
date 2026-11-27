// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

// S2.3.9: the Steam gate page shows every state of L2 steps 16 to 28 from the services, posts the
// services' updates through the dispatcher, drops the password at once, and shows no secret.
public sealed class SteamGateViewModelTests
{
    private const string Password = "pw-not-a-real-password";
    private const string Challenge = "https://example.invalid/q/challenge-one";
    private const string TokenKept = "in memory only, for this run";

    private readonly FakeServices _services = new();
    private readonly FakeSteamGateSteps _steps = new();
    private readonly FakeUiDispatcher _dispatcher = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpensSignedOutWithTheBuildLine()
    {
        var page = CreatePage();

        await page.LoadAsync(Token);

        Assert.Equal("Steam gate", page.Header);
        Assert.Equal("Release, Mono, Full ahead-of-time, interpreter: Off", page.BuildText);
        Assert.Equal("Signed out", page.SessionText);
        Assert.Equal("not checked yet", page.TokenStoredText);
        Assert.Equal("none yet", page.TransportText);
        Assert.Equal(1, _services.Steam.ResumeCalls);
        Assert.Equal(string.Empty, page.SignInTimesText);
        Assert.Contains("Build: Release, Mono, Full ahead-of-time, interpreter: Off", page.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumesWithTheSavedSignInWhenItLoads()
    {
        _services.Steam.ResumeResult = new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(2.5), TimeSpan.Zero, null);
        var page = CreatePage();

        await page.LoadAsync(Token);

        Assert.Equal("Logged on with the saved sign-in: total 2.5 s, waiting for you 0.0 s.", page.SignInTimesText);
        Assert.Contains("Sign-in with the saved sign-in: LoggedOn, total 2.5 s", page.Summary, StringComparison.Ordinal);
    }

    // The shell loads a page each time it shows it, and the heads keep one page per run. A second
    // load while logged on must not record the logon that is already there as a resume of 0.0 s.
    [Fact]
    public async Task OpeningThePageAgainWhileLoggedOnRecordsNoNewResume()
    {
        _services.Steam.ResumeResult = new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(3.2), TimeSpan.Zero, null);
        var page = CreatePage();
        await page.LoadAsync(Token);
        _services.Steam.Status = new SteamSessionStatus(SteamSessionState.LoggedOn, true, SteamTransport.WebSocket, null);

        // The real service answers a resume while logged on with zero times.
        _services.Steam.ResumeResult = new SignInResult(SignInOutcome.LoggedOn, TimeSpan.Zero, TimeSpan.Zero, null);
        await page.LoadAsync(Token);

        Assert.Equal(1, _services.Steam.ResumeCalls);
        Assert.Equal("Logged on with the saved sign-in: total 3.2 s, waiting for you 0.0 s.", page.SignInTimesText);
        Assert.Contains("Sign-in with the saved sign-in: LoggedOn, total 3.2 s", page.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("total 0.0 s", page.Summary, StringComparison.Ordinal);
    }

    // A session that gave up reconnecting is Offline with its sign-in still saved; opening the page
    // again resumes then, as it does while signed out.
    [Fact]
    public async Task OpeningThePageAgainWhileOfflineResumes()
    {
        var page = CreatePage();
        await page.LoadAsync(Token);
        _services.Steam.Status = new SteamSessionStatus(SteamSessionState.Offline, true, SteamTransport.WebSocket, null);
        _services.Steam.ResumeResult = new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(4.5), TimeSpan.Zero, null);

        await page.LoadAsync(Token);

        Assert.Equal(2, _services.Steam.ResumeCalls);
        Assert.Equal("Logged on with the saved sign-in: total 4.5 s, waiting for you 0.0 s.", page.SignInTimesText);
    }

    [Fact]
    public async Task OpeningThePageAgainDuringASignInDoesNotResume()
    {
        var page = await LoadedPageAsync();
        page.AccountName = "sample-name";
        page.Password = Password;
        var signIn = page.SignInCommand.ExecuteAsync(null);
        _services.Steam.ResumeResult = new SignInResult(SignInOutcome.LoggedOn, TimeSpan.Zero, TimeSpan.Zero, null);

        await page.LoadAsync(Token);

        Assert.Equal(1, _services.Steam.ResumeCalls);
        _services.Steam.LastAttempt!.End(new SignInResult(SignInOutcome.Cancelled, TimeSpan.FromSeconds(1), TimeSpan.Zero, null));
        await signIn;
        Assert.DoesNotContain("saved sign-in", page.Summary, StringComparison.Ordinal);
    }

    // The desktop run checks that the page reports the token as held in memory only
    // (C-0007 section 3); each head says where it keeps the sign-in.
    [Fact]
    public async Task ShowsWhereTheHeadKeepsTheSignIn()
    {
        var page = await LoadedPageAsync();

        Assert.Equal(TokenKept, page.TokenKeptText);
        Assert.Contains($"sign-in saved: not checked yet; kept: {TokenKept};", page.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInByApprovalDropsThePasswordAtOnceAndRecordsItsTimes()
    {
        var page = await LoadedPageAsync();
        page.AccountName = " sample-name ";
        page.Password = Password;

        var signIn = page.SignInCommand.ExecuteAsync(null);
        var attempt = _services.Steam.LastAttempt!;

        Assert.Equal(string.Empty, page.Password);
        Assert.Equal([("sample-name", Password, SignInConfirmation.ApproveInSteamApp)], _services.Steam.PasswordSignIns);
        attempt.Show(new SignInProgress(SignInStage.WaitingForApproval));
        Assert.Equal(1, _dispatcher.RunPending());
        Assert.True(page.IsWaitingForApproval);
        Assert.True(page.IsSigningIn);
        Assert.Equal("Approve the sign-in in the Steam mobile app, then come back.", page.SignInStageText);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(14.2), TimeSpan.FromSeconds(9.8), null));
        await signIn;

        Assert.False(page.IsSigningIn);
        Assert.False(page.IsWaitingForApproval);
        Assert.Equal("Logged on.", page.SignInStageText);
        Assert.Equal("Logged on with an approval: total 14.2 s, waiting for you 9.8 s.", page.SignInTimesText);
        Assert.Contains("Sign-in with an approval: LoggedOn, total 14.2 s, waiting for you 9.8 s", page.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInByCodePassesTheCodeOnAndClearsIt()
    {
        var page = await LoadedPageAsync();
        page.AccountName = "sample-name";
        page.Password = Password;
        page.EnterCodeInstead = true;

        var signIn = page.SignInCommand.ExecuteAsync(null);
        var attempt = _services.Steam.LastAttempt!;
        attempt.Show(new SignInProgress(SignInStage.WaitingForCode, SteamCodeKind.Email));
        _dispatcher.RunPending();
        Assert.True(page.IsWaitingForCode);
        Assert.Equal("Enter the code that Steam sent to your email.", page.SignInStageText);
        page.Code = "R7K2Q";
        page.SubmitCodeCommand.Execute(null);
        attempt.Show(new SignInProgress(SignInStage.WaitingForCode, SteamCodeKind.Email, CodeWasWrong: true));
        _dispatcher.RunPending();
        Assert.Equal("Steam refused the code. Enter the new one.", page.SignInStageText);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(25), null));
        await signIn;

        Assert.Equal(SignInConfirmation.EnterCode, _services.Steam.PasswordSignIns[0].Confirmation);
        Assert.Equal(["R7K2Q"], attempt.Codes);
        Assert.Equal(string.Empty, page.Code);
        Assert.Equal("Logged on with a code: total 30.0 s, waiting for you 25.0 s.", page.SignInTimesText);
    }

    [Fact]
    public async Task UsingACodeInsteadAndCancellingReachTheAttempt()
    {
        var page = await LoadedPageAsync();
        page.AccountName = "sample-name";
        page.Password = Password;

        var signIn = page.SignInCommand.ExecuteAsync(null);
        var attempt = _services.Steam.LastAttempt!;
        page.UseCodeInsteadCommand.Execute(null);
        page.CancelSignInCommand.Execute(null);
        await signIn;

        Assert.Equal(1, attempt.CodeInsteadRequests);
        Assert.True(attempt.WasCancelled);
        Assert.Equal("The sign-in was cancelled.", page.SignInStageText);
        Assert.Equal(string.Empty, page.SignInTimesText);
    }

    [Fact]
    public async Task AQrSignInOffersTheChallengeOnlyForDrawing()
    {
        var page = await LoadedPageAsync();

        var signIn = page.SignInWithQrCodeCommand.ExecuteAsync(null);
        var attempt = _services.Steam.LastAttempt!;
        attempt.Show(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: Challenge));
        _dispatcher.RunPending();
        Assert.True(page.IsShowingQrCode);
        Assert.Equal(Challenge, page.QrChallenge);
        attempt.Show(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: Challenge + "-two", QrRevision: 1));
        _dispatcher.RunPending();
        Assert.Equal(Challenge + "-two", page.QrChallenge);
        attempt.End(new SignInResult(SignInOutcome.LoggedOn, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(18), null));
        await signIn;

        Assert.Null(page.QrChallenge);
        Assert.False(page.IsShowingQrCode);
        Assert.Equal(1, _services.Steam.QrSignIns);
        Assert.Equal("Logged on with a QR code: total 20.0 s, waiting for you 18.0 s.", page.SignInTimesText);
        Assert.DoesNotContain("example.invalid", page.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", page.SignInStageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignInWithoutANameOrAPasswordDoesNotStart()
    {
        var page = await LoadedPageAsync();
        page.AccountName = "sample-name";

        await page.SignInCommand.ExecuteAsync(null);

        Assert.Empty(_services.Steam.PasswordSignIns);
        Assert.Equal("Enter an account name and a password.", page.SignInStageText);
    }

    [Fact]
    public async Task TheStepsShowWhatTheyGotAndNeverAValue()
    {
        var page = await LoadedPageAsync();

        await page.CountLicensesCommand.ExecuteAsync(null);
        page.AppIdText = " 4100000100 ";
        await page.ReadAppInfoCommand.ExecuteAsync(null);
        await page.GetDepotKeyCommand.ExecuteAsync(null);
        await page.GetManifestCodeCommand.ExecuteAsync(null);
        await page.DownloadManifestCommand.ExecuteAsync(null);
        await page.DownloadChunksCommand.ExecuteAsync(null);

        Assert.Null(page.StepProblem);
        Assert.Equal("42 licenses", page.LicensesText);
        Assert.Equal("Lantern Test App, 2 Windows depots.", page.AppInfoText);
        Assert.Equal(2, page.Depots.Count);
        Assert.Equal(4_100_000_113u, page.SelectedDepot!.Depot.DepotId);
        Assert.Equal("4100000113: no name, 2,048 bytes", page.SelectedDepot.Text);
        Assert.Equal("4100000111: Lantern Test App Content, 1,048,576 bytes, 64-bit", page.Depots[0].Text);
        Assert.Equal("depot key: obtained", page.DepotKeyText);
        Assert.Equal("manifest request code: obtained", page.ManifestCodeText);
        Assert.Equal("12 files, 30 chunks, 2,048 bytes, downloaded in 0.9 s", page.ManifestText);
        Assert.Equal("10 verified, 0 rejected, 0 failed of 10; 5,120 bytes downloaded, 10,240 bytes verified, in 1.2 s", page.ChunksText);
        Assert.Equal(
            ["licenses", "app info 4100000100", "depot key 4100000113", "manifest code 4100000113", "manifest 4100000113", "chunks 10"],
            _steps.Calls);
        foreach (var line in new[] { "Licenses: 42", "Depot key: obtained", "Manifest request code: obtained", "Manifest: 12 files", "Chunks: 10 verified" })
        {
            Assert.Contains(line, page.Summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AKeyOrACodeThatSteamDidNotGrantSaysSo()
    {
        _steps.DepotKey = SteamGrant.NotGranted;
        _steps.ManifestCode = SteamGrant.NotGranted;
        var page = await PageWithAppInfoAsync();

        await page.GetDepotKeyCommand.ExecuteAsync(null);
        await page.GetManifestCodeCommand.ExecuteAsync(null);

        Assert.Equal("depot key: not granted", page.DepotKeyText);
        Assert.Equal("manifest request code: not granted", page.ManifestCodeText);
    }

    [Fact]
    public async Task AStepProblemShowsItsOwnWordsOrOnlyItsType()
    {
        var page = await LoadedPageAsync();

        _steps.Failure = new SteamStepException("Sign in first.");
        await page.CountLicensesCommand.ExecuteAsync(null);
        Assert.Equal("Licenses: Sign in first.", page.StepProblem);

        _steps.Failure = new InvalidOperationException("a message with a server address in it");
        await page.CountLicensesCommand.ExecuteAsync(null);
        Assert.Equal("Licenses failed (InvalidOperationException).", page.StepProblem);
        Assert.DoesNotContain(_services.Log.Lines, line => line.Message.Contains("server address", StringComparison.Ordinal));

        await page.CountLicensesCommand.ExecuteAsync(null);
        Assert.Null(page.StepProblem);
    }

    [Fact]
    public async Task TheStepsAfterTheAppInfoNeedADepot()
    {
        var page = await LoadedPageAsync();

        page.AppIdText = "not a number";
        await page.ReadAppInfoCommand.ExecuteAsync(null);
        Assert.Equal("App info: Enter an app ID, a number above 0.", page.StepProblem);
        await page.GetDepotKeyCommand.ExecuteAsync(null);
        Assert.Equal("Depot key: Read an app's information and choose a depot first.", page.StepProblem);
        Assert.Empty(_steps.Calls);
    }

    [Fact]
    public async Task DeletingTheTestDataAndSigningOutReachTheServices()
    {
        var page = await LoadedPageAsync();

        await page.DeleteTestDataCommand.ExecuteAsync(null);
        await page.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(["delete"], _steps.Calls);
        Assert.Equal("The test data was deleted.", page.ChunksText);
        Assert.Equal(1, _services.Steam.SignOutCalls);
        Assert.Equal("no", page.TokenStoredText);
        Assert.Equal("Signed out.", page.SignInStageText);
    }

    [Fact]
    public async Task StatusChangesReachThePageThroughTheDispatcherWhileItIsShown()
    {
        var page = await LoadedPageAsync();
        page.NotifyShown();

        _services.Steam.Status = new SteamSessionStatus(SteamSessionState.LoggedOn, true, SteamTransport.WebSocket, Problem: null);
        Assert.Equal("Signed out", page.SessionText);
        _dispatcher.RunPending();

        Assert.Equal("Logged on", page.SessionText);
        Assert.Equal("yes", page.TokenStoredText);
        Assert.Equal("WebSocket", page.TransportText);
        Assert.Contains($"Session: Logged on; sign-in saved: yes; kept: {TokenKept}; transport: WebSocket", page.Summary, StringComparison.Ordinal);

        page.NotifyHidden();
        _services.Steam.Status = new SteamSessionStatus(SteamSessionState.Reconnecting, true, SteamTransport.Tcp, "Steam refused the logon: Busy (10).");
        Assert.Equal(0, _dispatcher.RunPending());
    }

    [Fact]
    public async Task ThePermissionPromptAndTheMemoryGoIntoTheSummary()
    {
        _services.Memory.Sample();
        var page = await LoadedPageAsync();

        page.PermissionPromptSeen = true;

        Assert.Contains("System permission prompt seen: yes", page.Summary, StringComparison.Ordinal);
        Assert.Contains("Peak memory footprint:", page.Summary, StringComparison.Ordinal);
        Assert.Contains("Highest thermal state: nominal", page.Summary, StringComparison.Ordinal);
    }

    private SteamGateViewModel CreatePage() =>
        new(_services.Steam, _steps, TokenKept, _services.Build, _services.Memory, _dispatcher, _services.Log);

    private async Task<SteamGateViewModel> LoadedPageAsync()
    {
        var page = CreatePage();
        await page.LoadAsync(Token);
        return page;
    }

    private async Task<SteamGateViewModel> PageWithAppInfoAsync()
    {
        var page = await LoadedPageAsync();
        page.AppIdText = "4100000100";
        await page.ReadAppInfoCommand.ExecuteAsync(null);
        return page;
    }
}
