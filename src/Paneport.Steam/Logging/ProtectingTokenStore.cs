// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Steam.Logging;

// Passes the token store's calls on, and registers the account name and the refresh token of
// every sign-in that goes through it with the log filter, so that a sign-in read back from the
// keychain at launch is protected as well as one that a sign-in just gave.
internal sealed class ProtectingTokenStore : ITokenStore
{
    private readonly ITokenStore _inner;
    private readonly RedactingLog _log;

    public ProtectingTokenStore(ITokenStore inner, RedactingLog log)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(log);
        _inner = inner;
        _log = log;
    }

    public async Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken)
    {
        var signIn = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        Protect(signIn);
        return signIn;
    }

    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        Protect(signIn);
        return _inner.WriteAsync(signIn, cancellationToken);
    }

    public Task DeleteAsync(CancellationToken cancellationToken) => _inner.DeleteAsync(cancellationToken);

    private void Protect(SavedSignIn? signIn)
    {
        if (signIn is not null)
        {
            _log.Protect(signIn.AccountName);
            _log.Protect(signIn.RefreshToken);
        }
    }
}
