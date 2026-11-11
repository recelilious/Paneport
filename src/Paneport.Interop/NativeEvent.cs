// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_event_t of paneport/host.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEvent
{
    public uint StructSize;
    public uint Kind;
    public long Code;
    public ulong Handle;
    public ulong Address;
}
