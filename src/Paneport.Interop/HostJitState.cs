// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The states of the JIT request protocol (<c>pp_jit_state_t</c>, C-0006 section 5), in the
/// order in which a request passes them.
/// </summary>
public enum HostJitState
{
    /// <summary>No debugger is attached, or only a foreign one is (<c>PP_JIT_STATE_NOT_DEBUGGED</c>).</summary>
    NotDebugged = 0,

    /// <summary>The broker that this launch asked for is attached; no pool has been requested yet.</summary>
    DebuggerAttached = 1,

    /// <summary>The pool is being requested.</summary>
    Requesting = 2,

    /// <summary>The pool is mapped, and the debugger is detaching.</summary>
    WaitingDetach = 3,

    /// <summary>The probe runs, with no debugger attached.</summary>
    Verifying = 4,

    /// <summary>The pool is authorized and verified.</summary>
    Ready = 5,

    /// <summary>A request, the detach, the probe or a timeout failed. Only a relaunch leaves it.</summary>
    Failed = 6,
}
