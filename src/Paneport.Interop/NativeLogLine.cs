// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_log_line_t of paneport/host.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeLogLine
{
    public uint StructSize;
    public int Level;
    public ulong ThreadId;
    public nint Text;
}
