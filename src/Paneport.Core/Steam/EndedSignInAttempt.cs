// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

// A sign-in attempt that failed before it started, such as one in a build without a Steam
// service. Its progress never changes.
internal sealed class EndedSignInAttempt : ISignInAttempt
{
    public EndedSignInAttempt(string problem)
    {
        Progress = new SignInProgress(SignInStage.Failed, Problem: problem);
        Completion = Task.FromResult(new SignInResult(SignInOutcome.Failed, TimeSpan.Zero, TimeSpan.Zero, problem));
    }

    event EventHandler? ISignInAttempt.ProgressChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public SignInProgress Progress { get; }

    public Task<SignInResult> Completion { get; }

    public void SubmitCode(string code)
    {
    }

    public void UseCodeInstead()
    {
    }

    public void Cancel()
    {
    }
}
