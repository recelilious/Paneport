// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop.Tests;

// A planted wrong definition of pp_memory_budget_t: the Game Mode flag comes after the limit
// instead of before it, which moves three members and the size.
[StructLayout(LayoutKind.Sequential)]
internal struct WrongMemoryBudget
{
    public uint StructSize;
    public ulong LimitBytes;
    public byte GameModeLevelReached;
    public ulong AvailableBytes;
    public ulong FootprintBytes;
}
