// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Desktop.Fakes;

// Keeps a Steam sign-in in memory for one run and nowhere else: the desktop head never writes a
// token to disk (C-0004 section 2, C-0007 section 3).
internal sealed class InMemoryTokenStore : ITokenStore
{
    private SavedSignIn? _signIn;

    public Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _signIn));
    }

    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _signIn, signIn);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _signIn, null);
        return Task.CompletedTask;
    }
}
