// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices.Marshalling;

namespace Paneport.Interop;

// What every safe call shares: turning a status into an exception with the host's message,
// and reading the host's UTF-8 text.
internal static unsafe class HostCalls
{
    // Throws when a call failed. The message comes from pp_last_error on the same thread,
    // before any other call into the host can replace it.
    internal static void Check(int status)
    {
        if (status != 0)
        {
            throw new HostException((HostStatus)status, Text(NativeMethods.LastError()));
        }
    }

    internal static string Text(byte* text) => Utf8StringMarshaller.ConvertToManaged(text) ?? string.Empty;

    internal static string Text(nint text) => Text((byte*)text);
}
