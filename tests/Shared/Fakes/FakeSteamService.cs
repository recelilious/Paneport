// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Tests.Fakes;

// A Steam service whose status a test sets and whose sign-ins it plays. It starts signed out, like
// the desktop head's, and records what the page asked of it.
internal sealed class FakeSteamService : ISteamService
{
    private readonly Lock _lock = new();
    private readonly List<(string AccountName, string Password, SignInConfirmation Confirmation)> _passwordSignIns = [];
    private SteamSessionStatus _status = SteamSessionStatus.Initial;

    public event EventHandler? StatusChanged;

    public SteamSignInState SignInState { get; set; } = SteamSignInState.SignedOut;

    public SteamSessionStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }

        set
        {
            lock (_lock)
            {
                _status = value;
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // What ResumeAsync gives; it completes at once unless a test holds it with ResumeGate.
    public SignInResult ResumeResult { get; set; } = SignInResult.NothingSaved;

    public TaskCompletionSource? ResumeGate { get; set; }

    public int ResumeCalls { get; private set; }

    public int QrSignIns { get; private set; }

    public int SignOutCalls { get; private set; }

    public FakeSignInAttempt? LastAttempt { get; private set; }

    public IReadOnlyList<(string AccountName, string Password, SignInConfirmation Confirmation)> PasswordSignIns
    {
        get
        {
            lock (_lock)
            {
                return [.. _passwordSignIns];
            }
        }
    }

    public async Task<SignInResult> ResumeAsync(CancellationToken cancellationToken)
    {
        ResumeCalls++;
        if (ResumeGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return ResumeResult;
    }

    public ISignInAttempt StartPasswordSignIn(string accountName, string password, SignInConfirmation confirmation)
    {
        lock (_lock)
        {
            _passwordSignIns.Add((accountName, password, confirmation));
        }

        LastAttempt = new FakeSignInAttempt();
        return LastAttempt;
    }

    public ISignInAttempt StartQrSignIn()
    {
        QrSignIns++;
        LastAttempt = new FakeSignInAttempt();
        return LastAttempt;
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        SignOutCalls++;
        Status = Status with { State = SteamSessionState.SignedOut, SignInSaved = false };
        return Task.CompletedTask;
    }
}
