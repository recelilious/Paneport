// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// What more a log line says: the <c>PP_LOG_FLAG_</c> bits of <c>paneport/host.h</c>, which version 1.5
/// of the interface added. A host older than version 1.5 sends none.
/// </summary>
[Flags]
public enum HostLogMarks
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>
    /// The line continues the one before it from the same source and process: a line of a Windows program
    /// over the host's limit of 511 bytes arrives in pieces cut at UTF-8 character boundaries, and each
    /// piece after the first has this flag; the text carries no mark of its own
    /// (<c>PP_LOG_FLAG_CONTINUATION</c>).
    /// </summary>
    Continuation = 1,

    /// <summary>
    /// The line reports a gap: <see cref="HostLogLine.DroppedLines"/> lines of this source and process were
    /// dropped, because the program wrote faster than the handler took them. The report comes where the lines
    /// are missing (<c>PP_LOG_FLAG_DROPPED</c>).
    /// </summary>
    Dropped = 2,
}
