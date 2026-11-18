// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Whether the call that turns off Mono's crash chaining resolved and returned at startup
/// (C-0005 section 4, docs/design/signals.md section 5). Diagnostics and the signal gate page of
/// the device test flavor show it; L2 step 11 checks it.
/// </summary>
/// <param name="Outcome">What became of the call.</param>
/// <param name="Failure">The type of the exception that the call threw, or null.</param>
public sealed record CrashChainingState(CrashChainingOutcome Outcome, string? Failure)
{
    private const string LogSource = "CrashChaining";

    /// <summary>Gets the state of a head that makes no such call.</summary>
    public static CrashChainingState NotCalled { get; } = new(CrashChainingOutcome.NotCalled, null);

    /// <summary>Gets the state as the app shows it.</summary>
    public string Description => Outcome switch
    {
        CrashChainingOutcome.TurnedOff => "Off: mono_set_crash_chaining(FALSE) resolved and returned",
        CrashChainingOutcome.NotResolved => $"Not turned off: mono_set_crash_chaining did not resolve ({Failure})",
        CrashChainingOutcome.Failed => $"Not turned off: the call failed ({Failure})",
        _ => "Not called: this head does not run on Mono",
    };

    /// <summary>
    /// Makes the call and records what became of it. It never throws: a startup that cannot turn
    /// crash chaining off goes on, and the signal gate's startup check shows the failure.
    /// </summary>
    /// <param name="turnOff">The binding that makes the call.</param>
    /// <param name="log">The log.</param>
    /// <returns>The state.</returns>
    public static CrashChainingState Run(Action turnOff, ILog log)
    {
        ArgumentNullException.ThrowIfNull(turnOff);
        ArgumentNullException.ThrowIfNull(log);
        CrashChainingState state;
        try
        {
            turnOff();
            state = new CrashChainingState(CrashChainingOutcome.TurnedOff, null);
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            state = new CrashChainingState(CrashChainingOutcome.NotResolved, exception.GetType().Name);
        }
        catch (Exception exception)
        {
            state = new CrashChainingState(CrashChainingOutcome.Failed, exception.GetType().Name);
        }

        log.Write(
            state.Outcome == CrashChainingOutcome.TurnedOff ? LogLevel.Information : LogLevel.Error,
            LogSource,
            $"Mono's crash chaining: {state.Description}.");
        return state;
    }
}
