// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>
/// The mark that excludes a folder from the device's backups. The iOS head sets it as the
/// folder's <c>NSURLIsExcludedFromBackupKey</c> resource value; iOS treats it as guidance
/// (C-0004 section 4).
/// </summary>
public interface IBackupMarks
{
    /// <summary>Marks a folder as excluded from backup.</summary>
    /// <param name="path">The folder's full path, which exists.</param>
    void SetExcluded(string path);

    /// <summary>Reads a folder's mark.</summary>
    /// <param name="path">The folder's full path.</param>
    /// <returns>True when the folder is marked, false when it is not, and null when the mark cannot be read.</returns>
    bool? IsExcluded(string path);
}
