// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// How far a title is installed on the device (C-0007 section 4).
/// </summary>
public enum InstallStatus
{
    /// <summary>The title is in the library, but none of its files are on the device.</summary>
    NotInstalled,

    /// <summary>The title's files are being downloaded.</summary>
    Downloading,

    /// <summary>The title is installed and up to date.</summary>
    Installed,

    /// <summary>
    /// The title is installed, but an update is pending. The title launches only after the update
    /// has finished (C-0007 section 4).
    /// </summary>
    UpdatePending,
}
