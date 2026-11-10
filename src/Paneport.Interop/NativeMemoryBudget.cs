// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_memory_budget_t of paneport/memory.h. The Game Mode flag is a uint8_t holding 0 or 1, as
// C-0005 section 1 has booleans cross the interface.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMemoryBudget
{
    public uint StructSize;
    public byte GameModeLevelReached;
    public ulong LimitBytes;
    public ulong AvailableBytes;
    public ulong FootprintBytes;
}
