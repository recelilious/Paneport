// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>Why the JIT state became <see cref="HostJitState.Failed"/> (<c>pp_jit_failure_t</c>).</summary>
public enum HostJitFailure
{
    /// <summary>The state is not <see cref="HostJitState.Failed"/> (<c>PP_JIT_FAILURE_NONE</c>).</summary>
    None = 0,

    /// <summary>One eighth of the memory limit is less than the smallest pool (C-0006 section 4).</summary>
    MemoryLimit = 1,

    /// <summary>The pool was not complete 60 seconds after the state became requesting.</summary>
    PoolTimeout = 2,

    /// <summary>The debugger had not detached 30 seconds after the host asked it to.</summary>
    DetachTimeout = 3,

    /// <summary>The debugger returned no region, or a region that is not executable and read-only.</summary>
    RegionRefused = 4,

    /// <summary>The host could not make a region's writable alias with the protections it needs.</summary>
    Mapping = 5,

    /// <summary>The debugger detached before the pool was complete.</summary>
    DebuggerLost = 6,

    /// <summary>A probed page did not run its test routine (C-0006 section 3 step 6).</summary>
    Verification = 7,

    /// <summary>The system refused something the host needs, such as a thread or the debugger flags.</summary>
    SystemFailure = 8,
}
