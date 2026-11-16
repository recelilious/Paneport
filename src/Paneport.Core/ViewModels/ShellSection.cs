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

    /// <summary>The host, the device and the build, as the app reads them.</summary>
    Diagnostics,

    /// <summary>The developer menu, which only the device test flavor offers (plan section 7.3).</summary>
    Developer,
}
