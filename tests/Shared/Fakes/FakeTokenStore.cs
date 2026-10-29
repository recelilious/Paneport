// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Tests.Fakes;

// A token store in memory. No page uses it yet; the shell's services need one.
internal sealed class FakeTokenStore : ITokenStore
{
    public Secret? Token { get; private set; }

    public Task<Secret?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Token);

    public Task WriteAsync(Secret token, CancellationToken cancellationToken)
    {
        Token = token;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        Token = null;
        return Task.CompletedTask;
    }
}
