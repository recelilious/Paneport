// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class SignedOutSteamServiceTests
{
    [Fact]
    public void IsAlwaysSignedOut()
    {
        var service = new SignedOutSteamService();

        Assert.Equal(SteamSignInState.SignedOut, service.SignInState);
        Assert.Equal(SteamSessionStatus.Initial, service.Status);
        Assert.Equal(SteamSessionState.SignedOut, service.Status.State);
        Assert.Null(service.Status.SignInSaved);
    }

    [Fact]
    public async Task FindsNothingToResume()
    {
        var result = await new SignedOutSteamService().ResumeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.NothingSaved, result.Outcome);
    }

    [Fact]
    public async Task EverySignInFailsAtOnceAndSaysWhy()
    {
        var service = new SignedOutSteamService();

        foreach (var attempt in new[] { service.StartPasswordSignIn("name", "password", SignInConfirmation.EnterCode), service.StartQrSignIn() })
        {
            Assert.Equal(SignInStage.Failed, attempt.Progress.Stage);
            Assert.True(attempt.Progress.HasEnded);
            Assert.Equal(SignedOutSteamService.NotAvailable, attempt.Progress.Problem);
            var result = await attempt.Completion;
            Assert.Equal(SignInOutcome.Failed, result.Outcome);
            Assert.Equal(SignedOutSteamService.NotAvailable, result.Problem);

            // Nothing that the page may send changes an attempt that has ended.
            attempt.SubmitCode("12345");
            attempt.UseCodeInstead();
            attempt.Cancel();
            Assert.Equal(SignInStage.Failed, attempt.Progress.Stage);
        }
    }

    [Fact]
    public async Task SigningOutDoesNothing()
    {
        var service = new SignedOutSteamService();

        await service.SignOutAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SteamSessionState.SignedOut, service.Status.State);
    }

    [Fact]
    public void AProgressTextLeavesOutTheQrChallenge()
    {
        var progress = new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: "https://example.invalid/q/challenge", QrRevision: 3);

        Assert.DoesNotContain("example.invalid", progress.ToString(), StringComparison.Ordinal);
        Assert.Contains("ShowingQrCode", progress.ToString(), StringComparison.Ordinal);
        Assert.False(progress.HasEnded);
    }
}
