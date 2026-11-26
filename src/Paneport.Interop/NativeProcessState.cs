// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// pp_process_state_t of paneport/session.h.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeProcessState
{
    public uint StructSize;
    public uint State;
    public uint ExitCode;
}
