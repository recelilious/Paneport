// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Tests.Fakes;

// A sign-in attempt that a test moves from stage to stage, recording what the page sent it.
internal sealed class FakeSignInAttempt : ISignInAttempt
{
    private readonly TaskCompletionSource<SignInResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _codes = [];

    public event EventHandler? ProgressChanged;

    public SignInProgress Progress { get; private set; } = new(SignInStage.Connecting);

    public Task<SignInResult> Completion => _completion.Task;

    public IReadOnlyList<string> Codes => [.. _codes];

    public int CodeInsteadRequests { get; private set; }

    public bool WasCancelled { get; private set; }

    public void SubmitCode(string code) => _codes.Add(code);

    public void UseCodeInstead() => CodeInsteadRequests++;

    public void Cancel()
    {
        WasCancelled = true;
        End(new SignInResult(SignInOutcome.Cancelled, TimeSpan.FromSeconds(1), TimeSpan.Zero, Problem: null));
    }

    public void Show(SignInProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void End(SignInResult result)
    {
        var stage = result.Outcome switch
        {
            SignInOutcome.LoggedOn => SignInStage.LoggedOn,
            SignInOutcome.Cancelled => SignInStage.Cancelled,
            _ => SignInStage.Failed,
        };
        Show(new SignInProgress(stage, Problem: result.Problem));
        _completion.TrySetResult(result);
    }
}
