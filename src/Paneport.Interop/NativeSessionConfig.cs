// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_session_config_t of paneport/session.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSessionConfig
{
    public uint StructSize;
    public uint Architecture;
    public nint PrefixFolder;
    public nint RuntimeFolder;
    public nint Environment;
}
