// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.ViewModels;

/// <summary>
/// The top-level sections that the shell's navigation offers. The shell shows the names as
/// they are written here.
/// </summary>
public enum ShellSection
{
    /// <summary>The titles in the library.</summary>
    Library,

    /// <summary>The app settings.</summary>
    Settings,

    /// <summary>The host's version and memory figures.</summary>
    Diagnostics,
}
