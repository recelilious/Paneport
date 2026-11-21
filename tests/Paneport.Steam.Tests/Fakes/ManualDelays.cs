// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// Waits that end only when a test releases them. A release runs what waited at once, on the
// test's thread, so a test sees its effect when the call returns.
internal sealed class ManualDelays : IDelays
{
    private readonly Lock _lock = new();
    private readonly List<TimeSpan> _requested = [];
    private readonly List<(TimeSpan Delay, TaskCompletionSource Done)> _pending = [];

    // Every wait that was asked for, in order.
    public IReadOnlyList<TimeSpan> Requested
    {
        get
        {
            lock (_lock)
            {
                return [.. _requested];
            }
        }
    }

    public IReadOnlyList<TimeSpan> Pending
    {
        get
        {
            lock (_lock)
            {
                return [.. _pending.Select(p => p.Delay)];
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource();
        var entry = (delay, done);
        lock (_lock)
        {
            _requested.Add(delay);
            _pending.Add(entry);
        }

        cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                _pending.Remove(entry);
            }

            done.TrySetCanceled(cancellationToken);
        });
        return done.Task;
    }

    // Ends the oldest pending wait of the given length.
    public void Release(TimeSpan delay)
    {
        TaskCompletionSource done;
        lock (_lock)
        {
            var index = _pending.FindIndex(p => p.Delay == delay);
            if (index < 0)
            {
                throw new InvalidOperationException($"No wait of {delay} is pending.");
            }

            done = _pending[index].Done;
            _pending.RemoveAt(index);
        }

        done.TrySetResult();
    }
}
