// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using Paneport.Core.Logging;
using Paneport.Core.Security;

namespace Paneport.Steam.Session;

// The service's only way to the token store (steam-service.md section 5): it saves the sign-in
// after a logon with a new token, reads it to resume, drops it when Steam refuses that sign-in's
// token and deletes it at sign-out. Nothing else is ever kept (C-0007 section 3). Its operations run one at
// a time in the order of the calls, so a drop that comes before a new sign-in's save can never
// delete the new token.
internal sealed class TokenKeeper : IDisposable
{
    private const string LogSource = "SteamTokens";

    private readonly ITokenStore _store;
    private readonly ILog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TokenKeeper(ITokenStore store, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
    }

    public async Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Saves a sign-in. The token cancels only the wait for the store: a sign-in cancelled before its
    // turn, such as by a sign-out, never writes, and a write that has begun completes, so that the
    // sign-out's delete, which comes after it, finds it.
    public async Task SaveAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.WriteAsync(signIn, CancellationToken.None).ConfigureAwait(false);
            _log.Write(LogLevel.Information, LogSource, "The sign-in was saved.");
        }
        finally
        {
            _gate.Release();
        }
    }

    // At sign-out.
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.DeleteAsync(cancellationToken).ConfigureAwait(false);
            _log.Write(LogLevel.Information, LogSource, "The saved sign-in was deleted.");
        }
        finally
        {
            _gate.Release();
        }
    }

    // After Steam refused a sign-in's token: deletes the saved sign-in only when it is that sign-in,
    // so that a fresh token that Steam refuses leaves another account's saved sign-in alone. It
    // cannot be cancelled, and a failure is logged with its type, because nothing waits for it.
    // Returns whether it deleted the saved sign-in.
    public async Task<bool> DropAsync(SavedSignIn refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var saved = await _store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (!Same(saved, refused))
            {
                _log.Write(LogLevel.Information, LogSource, "Steam refused a token that is not the saved one, so the saved sign-in stays.");
                return false;
            }

            await _store.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
            _log.Write(LogLevel.Warning, LogSource, "Steam refused the saved token, so the saved sign-in was dropped.");
            return true;
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Error, LogSource, $"Dropping the saved sign-in failed with {exception.GetType().Name}.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    // Whether two sign-ins are the same: the same account name and the same token, compared in
    // constant time. A sign-in read from the store again is a new object with the same values.
    public static bool Same(SavedSignIn? first, SavedSignIn? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null)
        {
            return false;
        }

        return string.Equals(first.AccountName, second.AccountName, StringComparison.Ordinal)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(first.RefreshToken.Reveal()),
                Encoding.UTF8.GetBytes(second.RefreshToken.Reveal()));
    }
}
