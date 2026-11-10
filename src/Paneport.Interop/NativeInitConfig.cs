// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_init_config_t of paneport/host.h. Pointers are nint, so that LayoutConformance can measure
// them like any other member.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeInitConfig
{
    public uint StructSize;
    public nint CrashRecordFolder;
}
