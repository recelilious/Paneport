// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// A QR sign-in whose challenge the test changes and whose scan the test makes.
internal sealed class FakeQrSession : IQrAuthSession
{
    private readonly AuthTokens _tokens;
    private readonly TaskCompletionSource<AuthTokens> _scan = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _challenge;

    public FakeQrSession(AuthTokens tokens, string challenge)
    {
        _tokens = tokens;
        _challenge = challenge;
    }

    public event EventHandler? ChallengeChanged;

    public string Challenge => Volatile.Read(ref _challenge);

    public void ChangeChallenge(string challenge)
    {
        Volatile.Write(ref _challenge, challenge);
        ChallengeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Scan() => _scan.TrySetResult(_tokens);

    public Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken) => _scan.Task.WaitAsync(cancellationToken);
}
