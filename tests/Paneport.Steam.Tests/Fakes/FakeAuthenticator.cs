// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// Steam's authentication service as SteamKit2 presents it, played by the test. A credentials
// session asks the code source as SteamKit2 does: once whether to wait for an approval, when the
// account allows one, and then for codes until one is right. The test approves or declines the
// approval, and scans the QR code.
internal sealed class FakeAuthenticator : ISteamAuthenticator
{
    private readonly Lock _lock = new();
    private readonly SteamSamples _samples;
    private readonly List<string> _passwords = [];
    private TaskCompletionSource? _approval;
    private FakeQrSession? _qr;

    public FakeAuthenticator(SteamSamples samples) => _samples = samples;

    // What the account asks for: an approval with a code as its fallback, a code from the
    // authenticator, an email code, or nothing.
    public AccountGuard Guard { get; set; } = AccountGuard.Approval;

    // The code that Steam accepts.
    public string Code { get; set; } = "R7K2Q";

    // When set, Steam refuses each credentials session at its start with this result.
    public SteamResult? RefuseStart { get; set; }

    // The passwords that the credentials sessions received, in order.
    public IReadOnlyList<string> Passwords
    {
        get
        {
            lock (_lock)
            {
                return [.. _passwords];
            }
        }
    }

    public int CredentialSessions => Passwords.Count;

    public FakeQrSession Qr => _qr ?? throw new InvalidOperationException("No QR session began.");

    public Task<IAuthSession> BeginWithCredentialsAsync(string accountName, string password, ICodeSource codes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RefuseStart is { } refusal)
        {
            throw new SteamAuthException(refusal);
        }

        lock (_lock)
        {
            _passwords.Add(password);
        }

        return Task.FromResult<IAuthSession>(new CredentialsSession(this, codes));
    }

    public Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = new FakeQrSession(Tokens(), "https://example.invalid/q/first");
        _qr = session;
        return Task.FromResult<IQrAuthSession>(session);
    }

    // Approves the open request for an approval in the Steam mobile app.
    public void Approve() => TakeApproval().TrySetResult();

    // Ends the open request for an approval with Steam's result, as a denial or an expiry would.
    public void DeclineApproval(SteamResult result) => TakeApproval().TrySetException(new SteamAuthException(result));

    // Whether a request for an approval is open: from the start of a credentials session, when the
    // account allows an approval, until the test answers it, the flow chooses a code or the wait ends.
    public bool WaitsForApproval
    {
        get
        {
            lock (_lock)
            {
                return _approval is not null;
            }
        }
    }

    private AuthTokens Tokens() => new(_samples.AccountName, _samples.RefreshTokenSecret);

    private TaskCompletionSource TakeApproval()
    {
        lock (_lock)
        {
            var approval = _approval ?? throw new InvalidOperationException("No approval is awaited.");
            _approval = null;
            return approval;
        }
    }

    // Steam sends the request to the Steam mobile app when a credentials session begins, so the
    // test can answer it from then on, even before the session waits for the answer. The flow shows
    // that it waits for the approval before the session waits, as SteamKit2 asks the code source
    // first and polls afterwards.
    private TaskCompletionSource OfferApproval()
    {
        var approval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _approval = approval;
        }

        return approval;
    }

    private void WithdrawApproval(TaskCompletionSource approval)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_approval, approval))
            {
                _approval = null;
            }
        }
    }

    private async Task WaitForApprovalAsync(TaskCompletionSource approval, CancellationToken cancellationToken)
    {
        try
        {
            await approval.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WithdrawApproval(approval);
        }
    }

    private sealed class CredentialsSession(FakeAuthenticator owner, ICodeSource codes) : IAuthSession
    {
        public async Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken)
        {
            var kind = SteamCodeKind.Authenticator;
            switch (owner.Guard)
            {
                case AccountGuard.None:
                    return owner.Tokens();
                case AccountGuard.Approval:
                    var approval = owner.OfferApproval();
                    if (await codes.AcceptApprovalAsync().ConfigureAwait(false))
                    {
                        await owner.WaitForApprovalAsync(approval, cancellationToken).ConfigureAwait(false);
                        return owner.Tokens();
                    }

                    owner.WithdrawApproval(approval);
                    break;
                case AccountGuard.Email:
                    kind = SteamCodeKind.Email;
                    break;
                default:
                    break;
            }

            var previousWasWrong = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var code = await codes.GetCodeAsync(kind, previousWasWrong).ConfigureAwait(false);
                if (code == owner.Code)
                {
                    return owner.Tokens();
                }

                previousWasWrong = true;
            }
        }
    }
}
