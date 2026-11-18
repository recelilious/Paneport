// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_test_signal_harness_config_t of paneport/testing.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSignalHarnessConfig
{
    public uint StructSize;
    public uint MarkedThreads;
    public uint UnmarkedThreads;
    public uint PauseMicroseconds;
}
