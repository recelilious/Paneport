// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Session;

// Runs actions one at a time, in the order in which they were posted. The thread that finds the
// queue idle runs it until it is empty; a post from inside an action, or from another thread
// while the queue runs, waits its turn instead of running at once. State that only these actions
// touch needs no other lock, and no action ever runs inside another.
internal sealed class SerialQueue
{
    private readonly Lock _lock = new();
    private readonly Queue<Action> _actions = new();
    private readonly Action<Exception> _onFailure;
    private bool _running;

    // onFailure receives an exception that an action threw; the queue goes on with the next one.
    public SerialQueue(Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        _onFailure = onFailure;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_lock)
        {
            _actions.Enqueue(action);
            if (_running)
            {
                return;
            }

            _running = true;
        }

        while (true)
        {
            Action next;
            lock (_lock)
            {
                if (!_actions.TryDequeue(out var queued))
                {
                    _running = false;
                    return;
                }

                next = queued;
            }

            try
            {
                next();
            }
            catch (Exception exception)
            {
                _onFailure(exception);
            }
        }
    }
}
