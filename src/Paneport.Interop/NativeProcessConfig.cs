// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_process_config_t of paneport/session.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeProcessConfig
{
    public uint StructSize;
    public byte CaptureOutput;
    public nint ProgramFolder;
    public nint Program;
    public nint Arguments;
}
