// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_test_signal_counters_t of paneport/testing.h. The running flag is a uint8_t holding 0 or 1,
// as C-0005 section 1 has booleans cross the interface.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSignalCounters
{
    public uint StructSize;
    public byte Running;
    public ulong GuestFaultsRaisedByMark;
    public ulong GuestFaultsHandledByMark;
    public ulong GuestFaultsRaisedByAddress;
    public ulong GuestFaultsHandledByAddress;
    public ulong UnexpectedFaults;
    public ulong RuntimeFaults;
}
