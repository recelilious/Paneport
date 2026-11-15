// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>What the crash record reader found.</summary>
public enum CrashRecordStatus
{
    /// <summary>No record: the previous run ended without a fatal fault, or the record was dismissed.</summary>
    None,

    /// <summary>A record of the documented layout.</summary>
    Valid,

    /// <summary>A file that is not a record of the documented layout, such as a truncated one.</summary>
    Malformed,
}
