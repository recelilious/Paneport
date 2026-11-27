// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Steam.Session;

namespace Paneport.Steam.Logging;

// Passes the token store's calls on, and keeps the account name and the refresh token of the
// sign-in that the store holds registered with the log filter, so that a sign-in read back from the
// keychain at launch is protected as well as one that a sign-in just gave. It keeps one registration
// for that sign-in, however often the store is read or written, and takes it out when the store no
// longer holds the sign-in: after a delete, as at sign-out or when Steam refused the saved token,
// or when another sign-in takes its place.
internal sealed class ProtectingTokenStore : ITokenStore
{
    private readonly ITokenStore _inner;
    private readonly RedactingLog _log;
    private readonly Lock _lock = new();
    private SavedSignIn? _held;

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
        Hold(signIn);
        return signIn;
    }

    // The sign-in is registered before the store writes it.
    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        Hold(signIn);
        return _inner.WriteAsync(signIn, cancellationToken);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await _inner.DeleteAsync(cancellationToken).ConfigureAwait(false);
        Hold(null);
    }

    // Moves the registration to the sign-in that the store holds now. The new values are registered
    // before the old ones are taken out, so that a value that both share is never out of the filter.
    private void Hold(SavedSignIn? signIn)
    {
        lock (_lock)
        {
            if (TokenKeeper.Same(_held, signIn))
            {
                return;
            }

            if (signIn is not null)
            {
                _log.Protect(signIn.AccountName);
                _log.Protect(signIn.RefreshToken);
            }

            if (_held is not null)
            {
                _log.Unprotect(_held.AccountName);
                _log.Unprotect(_held.RefreshToken);
            }

            _held = signIn;
        }
    }
}
