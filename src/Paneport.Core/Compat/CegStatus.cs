// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// Whether a title uses Steam's Custom Executable Generation, the <c>ceg</c> member of
/// <c>drm</c> (C-0007 section 5, C-0008 section 2).
/// </summary>
/// <remarks>
/// CEG cannot be detected by reading files. The device's classification therefore always records
/// <see cref="Unknown"/>, and only a reviewed entry sets <see cref="Yes"/> or <see cref="No"/>.
/// The default value is <see cref="Unknown"/>.
/// </remarks>
public enum CegStatus
{
    /// <summary>Whether the title uses CEG is not known.</summary>
    Unknown,

    /// <summary>The title uses CEG.</summary>
    Yes,

    /// <summary>The title does not use CEG.</summary>
    No,
}
