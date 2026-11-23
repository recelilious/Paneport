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

    // Version 1.3 appended these; a line of an older host ends before them.
    public uint Source;
    public ulong Process;
}
