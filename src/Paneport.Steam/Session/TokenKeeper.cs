// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Security;

namespace Paneport.Steam.Session;

// The service's only way to the token store (steam-service.md section 5): it saves the sign-in
// after a logon with a new token, reads it to resume, drops it when Steam refuses the token and
// deletes it at sign-out. Nothing else is ever kept (C-0007 section 3). Its operations run one at
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

    public async Task SaveAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.WriteAsync(signIn, cancellationToken).ConfigureAwait(false);
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

    // After Steam refused the saved token. It cannot be cancelled, and a failure is logged with its
    // type, because nothing waits for it.
    public async Task DropAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _store.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
            _log.Write(LogLevel.Warning, LogSource, "Steam refused the saved token, so the saved sign-in was dropped.");
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Error, LogSource, $"Dropping the saved sign-in failed with {exception.GetType().Name}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
