// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_jit_status_t of paneport/jit.h, which version 1.3 added. The four flags are uint8_t values
// holding 0 or 1, as C-0005 section 1 has booleans cross the interface.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeJitStatus
{
    public uint StructSize;
    public int State;
    public int Failure;
    public byte DebuggedSinceLaunch;
    public byte AttachedNow;
    public byte EnablerOpened;
    public byte ForeignDebugger;
    public uint RequestedMiB;
    public uint PoolMiB;
    public uint RegionsPlanned;
    public uint RegionsMapped;
    public ulong InStateMilliseconds;
    public ulong DebuggerAttachedMilliseconds;
    public ulong RequestingMilliseconds;
    public ulong WaitingDetachMilliseconds;
    public ulong VerifyingMilliseconds;
    public ulong AuthorizationMilliseconds;
}
