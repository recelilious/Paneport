// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The counters of the signal gate's native stress harness and of the signal dispatcher, as
/// <c>pp_test_signal_harness_read</c> reports them (docs/design/signals.md section 9).
/// </summary>
/// <param name="Running">Whether the harness's threads run.</param>
/// <param name="GuestFaultsRaisedByMark">Faults that the marked threads planted.</param>
/// <param name="GuestFaultsHandledByMark">Faults that the harness's guest fault handler took by the thread's mark.</param>
/// <param name="GuestFaultsRaisedByAddress">Faults that the unmarked threads planted in the reservation.</param>
/// <param name="GuestFaultsHandledByAddress">Faults that the harness's guest fault handler took by their address.</param>
/// <param name="UnexpectedFaults">Faults that took a route the harness did not plant them for.</param>
/// <param name="RuntimeFaults">Faults of any thread that the dispatcher passed to the runtime's handler.</param>
public readonly record struct SignalHarnessCounters(
    bool Running,
    ulong GuestFaultsRaisedByMark,
    ulong GuestFaultsHandledByMark,
    ulong GuestFaultsRaisedByAddress,
    ulong GuestFaultsHandledByAddress,
    ulong UnexpectedFaults,
    ulong RuntimeFaults);
