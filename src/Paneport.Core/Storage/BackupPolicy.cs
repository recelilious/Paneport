// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>
/// Whether a folder of the storage layout goes into the device's backups: the "In backups"
/// column of C-0004 section 4.
/// </summary>
public enum BackupPolicy
{
    /// <summary>"No": Paneport marks the folder as excluded from backup, and marks it again after replacing files in it.</summary>
    Excluded,

    /// <summary>"Yes": the folder is backed up, and Paneport sets no mark.</summary>
    Included,

    /// <summary>"User's choice": the user's own files, which the Files app shows; Paneport sets no mark.</summary>
    UserChoice,
}
