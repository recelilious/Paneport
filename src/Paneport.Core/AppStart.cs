// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.ViewModels;

namespace Paneport.Core;

/// <summary>
/// Starts the app's run once per process: the first sample of the memory timeline, the timer that
/// samples it from then on, and the shell's first load (docs/design/ios-app-head.md section 3).
/// </summary>
/// <remarks>
/// A head calls <see cref="TryStart"/> on the UI thread once the shell's view is on screen. A later
/// call does nothing: iOS may disconnect a scene and connect it again in the same process, which
/// gives it a new root view controller, and the timeline must keep one sample a second and the
/// shell its page. The timer runs for the rest of the process.
/// </remarks>
public sealed class AppStart
{
    private readonly ShellViewModel _shell;
    private readonly MemoryMonitor _memory;
    private readonly Func<Func<bool>, TimeSpan, IDisposable> _runTimer;
    private IDisposable? _timer;

    /// <summary>Prepares the start of a run.</summary>
    /// <param name="shell">The shell, whose start command runs once.</param>
    /// <param name="memory">The memory timeline.</param>
    /// <param name="runTimer">
    /// Starts a timer on the UI thread that calls a function at an interval for as long as it returns
    /// true, as Avalonia's <c>DispatcherTimer.Run</c> does, and returns what stops the timer.
    /// </param>
    public AppStart(ShellViewModel shell, MemoryMonitor memory, Func<Func<bool>, TimeSpan, IDisposable> runTimer)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(runTimer);
        _shell = shell;
        _memory = memory;
        _runTimer = runTimer;
    }

    /// <summary>Gets a value indicating whether the run has started.</summary>
    public bool Started => _timer is not null;

    /// <summary>Starts the run, unless it has started already. UI thread only.</summary>
    /// <returns>True when this call started the run.</returns>
    public bool TryStart()
    {
        if (_timer is not null)
        {
            return false;
        }

        _memory.Sample();
        _timer = _runTimer(SampleMemory, MemoryMonitor.Interval);
        _shell.StartCommand.Execute(null);
        return true;
    }

    private bool SampleMemory()
    {
        _memory.Sample();
        return true;
    }
}
