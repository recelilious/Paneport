// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>The constants of the JIT group of the host interface (<c>paneport/jit.h</c>, C-0006).</summary>
public static class HostJit
{
    /// <summary>
    /// The kind of the event that reports a change of the JIT state
    /// (<c>PP_EVENT_JIT_STATE_CHANGED</c>); its code holds the new <see cref="HostJitState"/>.
    /// </summary>
    public const uint StateChangedEvent = 1;

    /// <summary>The pool size that the app asks for when the title has no <c>jitPoolMiB</c> (C-0006 section 4).</summary>
    public const uint DefaultPoolMiB = 512;

    /// <summary>The smallest pool, in MiB.</summary>
    public const uint MinimumPoolMiB = 256;

    /// <summary>The largest pool, in MiB, which is also never more than one eighth of the memory limit.</summary>
    public const uint MaximumPoolMiB = 896;

    /// <summary>The time the pool may take after the state became requesting, in milliseconds.</summary>
    public const uint PoolTimeoutMilliseconds = 60_000;

    /// <summary>The time the debugger may take to detach after the host asked it to, in milliseconds.</summary>
    public const uint DetachTimeoutMilliseconds = 30_000;
}
