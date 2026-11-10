// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// Whether the bundled entry's classification counts for a title, and why not when it does not. It
/// counts only while the device runs the program that the entry was reviewed for (C-0008 section 1).
/// </summary>
/// <remarks>
/// Where it does not count, only the computed classification does. Its <c>ceg</c> is never
/// <c>no</c>, so once classified the title takes the Dock path or is refused, and a title's page
/// can say why. The default value is <see cref="None"/>.
/// </remarks>
public enum ReviewedClassificationUse
{
    /// <summary>No bundled entry sets a <c>drm</c> member, so there is nothing reviewed to use.</summary>
    None,

    /// <summary>
    /// The device runs the reviewed program, so the bundled entry's <c>drm</c> members win over the
    /// computed ones.
    /// </summary>
    Used,

    /// <summary>
    /// A Steam title whose device layer names another program than the bundled entry, such as a
    /// launcher, or names one where the bundled entry names none.
    /// </summary>
    OtherProgram,

    /// <summary>
    /// A local title whose device entry lists a build that the bundled entry does not, or that has
    /// no device entry yet to show which build the device runs.
    /// </summary>
    UnreviewedBuild,
}
