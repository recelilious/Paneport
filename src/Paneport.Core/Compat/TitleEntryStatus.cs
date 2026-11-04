// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The outcome of reading one title file.
/// </summary>
public enum TitleEntryStatus
{
    /// <summary>The layer has no file for the title.</summary>
    Missing,

    /// <summary>The file was read, and the entry follows every rule of its layer.</summary>
    Loaded,

    /// <summary>The file breaks a rule, so none of its values are used.</summary>
    Rejected,
}
