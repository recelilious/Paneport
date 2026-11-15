// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Tests.Fakes;

// A token store in memory. No page uses it yet; the shell's services need one.
internal sealed class FakeTokenStore : ITokenStore
{
    public SavedSignIn? SignIn { get; private set; }

    public Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(SignIn);

    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        SignIn = signIn;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        SignIn = null;
        return Task.CompletedTask;
    }
}
