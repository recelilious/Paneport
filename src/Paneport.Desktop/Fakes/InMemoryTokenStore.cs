// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Desktop.Fakes;

// Keeps a Steam token in memory for one run and nowhere else: the desktop head never writes a
// token to disk (C-0004 section 2, C-0007 section 3).
internal sealed class InMemoryTokenStore : ITokenStore
{
    private Secret? _token;

    public Task<Secret?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _token));
    }

    public Task WriteAsync(Secret token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _token, token);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _token, null);
        return Task.CompletedTask;
    }
}
