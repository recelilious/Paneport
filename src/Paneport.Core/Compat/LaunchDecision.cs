// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// What the launch rule of C-0008 section 5 makes of a title's effective classification.
/// </summary>
/// <remarks>
/// <see cref="LaunchRule.Decide"/> derives it. The default value is <see cref="Unclassified"/>.
/// </remarks>
public enum LaunchDecision
{
    /// <summary>
    /// The classification lacks <c>steamApi</c> or <c>steamStub</c> and shows nothing that blocks
    /// the Direct path yet, so the title is classified before its first launch (C-0007 section 5).
    /// </summary>
    Unclassified,

    /// <summary>
    /// <c>steamApi</c> and <c>steamStub</c> are false and <c>ceg</c> is <c>no</c>: the title's program
    /// starts directly in the Wine session.
    /// </summary>
    Direct,

    /// <summary>
    /// A Steam title whose classification does not allow the Direct path: it starts through the
    /// Dock host.
    /// </summary>
    Dock,

    /// <summary>
    /// A local title whose classification does not allow the Direct path. A local title can take
    /// no other path, so the app refuses to launch it (C-0008 section 5).
    /// </summary>
    Refused,
}
