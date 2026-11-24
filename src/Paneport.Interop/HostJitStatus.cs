// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The JIT state and what the host knows about the request (<c>pp_jit_status_t</c>, C-0006).
/// Durations are in milliseconds of the host's monotonic clock.
/// </summary>
/// <param name="State">The state.</param>
/// <param name="Failure">Why the state is <see cref="HostJitState.Failed"/>, or <see cref="HostJitFailure.None"/>.</param>
/// <param name="DebuggedSinceLaunch">Whether the last reading found the code-signing flag <c>CS_DEBUGGED</c>.</param>
/// <param name="AttachedNow">Whether the last reading found the process's traced flag <c>P_TRACED</c>.</param>
/// <param name="EnablerOpened">Whether the app said that this launch opened the JIT enabler.</param>
/// <param name="ForeignDebugger">Whether a debugger is attached that the host does not take for the broker.</param>
/// <param name="RequestedMiB">The size that the pool request asked for, or 0 before it.</param>
/// <param name="PoolMiB">The pool's size after the limits of C-0006 section 4, or 0.</param>
/// <param name="RegionsPlanned">How many regions the pool takes.</param>
/// <param name="RegionsMapped">How many regions are prepared and mapped so far.</param>
/// <param name="InStateMilliseconds">How long the current state has lasted so far.</param>
/// <param name="DebuggerAttachedMilliseconds">How long the state was debugger_attached, once it left it, in total.</param>
/// <param name="RequestingMilliseconds">How long the state was requesting, once it left it.</param>
/// <param name="WaitingDetachMilliseconds">How long the state was waiting_detach, once it left it.</param>
/// <param name="VerifyingMilliseconds">How long the state was verifying, once it left it.</param>
/// <param name="AuthorizationMilliseconds">The time from the request until the state became ready, or 0 before it.</param>
public readonly record struct HostJitStatus(
    HostJitState State,
    HostJitFailure Failure,
    bool DebuggedSinceLaunch,
    bool AttachedNow,
    bool EnablerOpened,
    bool ForeignDebugger,
    uint RequestedMiB,
    uint PoolMiB,
    uint RegionsPlanned,
    uint RegionsMapped,
    ulong InStateMilliseconds,
    ulong DebuggerAttachedMilliseconds,
    ulong RequestingMilliseconds,
    ulong WaitingDetachMilliseconds,
    ulong VerifyingMilliseconds,
    ulong AuthorizationMilliseconds);
