// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_test_layout_member_t of paneport/testing.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeLayoutMember
{
    public nint Name;
    public uint Offset;
    public uint Size;
}
