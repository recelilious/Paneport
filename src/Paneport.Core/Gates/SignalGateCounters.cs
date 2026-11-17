// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Gates;

/// <summary>
/// What a run of the signal gate counted (L2 steps 12 and 13, docs/design/signals.md section 9).
/// </summary>
public sealed record SignalGateCounters
{
    /// <summary>Gets how long the run lasted.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets a value indicating whether the app stayed in the foreground for the whole run.</summary>
    public bool StayedInForeground { get; init; }

    /// <summary>Gets the managed null-reference exceptions that the loop threw.</summary>
    public ulong ExceptionsThrown { get; init; }

    /// <summary>Gets the managed null-reference exceptions that the loop caught.</summary>
    public ulong ExceptionsCaught { get; init; }

    /// <summary>Gets the guest faults that the marked threads planted.</summary>
    public ulong GuestFaultsRaisedByMark { get; init; }

    /// <summary>Gets the guest faults that the guest path handled by the thread's mark.</summary>
    public ulong GuestFaultsHandledByMark { get; init; }

    /// <summary>Gets the guest faults that the unmarked threads planted in the reservation.</summary>
    public ulong GuestFaultsRaisedByAddress { get; init; }

    /// <summary>Gets the guest faults that the guest path handled by their address.</summary>
    public ulong GuestFaultsHandledByAddress { get; init; }

    /// <summary>Gets the faults that took a route they were not planted for.</summary>
    public ulong UnexpectedFaults { get; init; }

    /// <summary>Gets the faults that the dispatcher passed to the runtime's handler.</summary>
    public ulong RuntimeFaults { get; init; }

    /// <summary>Gets the garbage collections during the run, of every generation.</summary>
    public long GarbageCollections { get; init; }

    /// <summary>Gets the longest time between two heartbeats, the current gap included.</summary>
    public TimeSpan LongestHeartbeatGap { get; init; }

    /// <summary>Gets a value indicating whether the dispatcher still had all six signals at the end.</summary>
    public bool DispatcherInstalled { get; init; }
}
