// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Tests.Fakes;

// A token store in memory that records every call, so that a test can check what was kept.
internal sealed class FakeTokenStore : ITokenStore
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<SavedSignIn> _writes = [];
    private SavedSignIn? _signIn;

    public SavedSignIn? SignIn
    {
        get
        {
            lock (_lock)
            {
                return _signIn;
            }
        }

        set
        {
            lock (_lock)
            {
                _signIn = value;
            }
        }
    }

    // The calls in order: "read", "write" and "delete".
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    // Every sign-in that was written, in order.
    public IReadOnlyList<SavedSignIn> Writes
    {
        get
        {
            lock (_lock)
            {
                return [.. _writes];
            }
        }
    }

    // Holds each read until a test completes it, so that a test can keep the store busy.
    public TaskCompletionSource? ReadGate { get; set; }

    public async Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add("read");
        }

        if (ReadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        lock (_lock)
        {
            return _signIn;
        }
    }

    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add("write");
            _writes.Add(signIn);
            _signIn = signIn;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _calls.Add("delete");
            _signIn = null;
        }

        return Task.CompletedTask;
    }
}
