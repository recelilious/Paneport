// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// Where a title comes from, which decides how it is identified (C-0008 section 1).
/// </summary>
public enum TitleSource
{
    /// <summary>A title from the user's Steam library, identified by its Steam app ID.</summary>
    Steam,

    /// <summary>A title that the user brought in without Steam, identified by a local slug.</summary>
    Local,
}
