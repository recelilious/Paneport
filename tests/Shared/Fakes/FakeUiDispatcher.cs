// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Paneport.Core.Hosting;

namespace Paneport.Tests.Fakes;

// A UI dispatcher that queues work from any thread until the test runs it on its own thread, which
// then stands in for the UI thread.
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();

    public int Pending => _queue.Count;

    public void Post(Action action) => _queue.Enqueue(action);

    public int RunPending()
    {
        var count = 0;
        while (_queue.TryDequeue(out var action))
        {
            action();
            count++;
        }

        return count;
    }
}
