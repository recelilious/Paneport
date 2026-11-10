// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Paneport.Interop.Tests;

/// <summary>
/// A thread that stands in for the app's main thread. The test runner runs tests on worker
/// threads, and the host's test builds take the thread that first calls <c>pp_init</c> as the
/// main thread, so every main-thread call of the tests runs here.
/// </summary>
public sealed class HostThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    /// <summary>Starts the thread.</summary>
    public HostThread()
    {
        _thread = new Thread(Work) { IsBackground = true, Name = "Host main thread of the tests" };
        _thread.Start();
    }

    /// <summary>Gets the managed identifier of the thread.</summary>
    public int ManagedThreadId => _thread.ManagedThreadId;

    /// <summary>Runs an action on the thread, waits for it, and throws what it threw.</summary>
    /// <param name="action">The action.</param>
    public void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim();
        _work.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        failure?.Throw();
    }

    /// <summary>Stops the thread once its work is done.</summary>
    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join();
        _work.Dispose();
    }

    private void Work()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            action();
        }
    }
}
