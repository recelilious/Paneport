// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// How well a title runs, the <c>rating</c> of an entry or of a report (C-0008 section 3).
/// </summary>
/// <remarks>
/// The default value, <see cref="Untested"/>, is also the schema default: a title with no entry
/// shows as untested (C-0008 section 1). A report never observes <see cref="Untested"/>.
/// </remarks>
public enum CompatRating
{
    /// <summary>No report counts yet, written <c>untested</c>.</summary>
    Untested,

    /// <summary>
    /// Completes the main story at its target frame rate with no known issues, written
    /// <c>perfect</c>.
    /// </summary>
    Perfect,

    /// <summary>Can be played through, with minor issues, written <c>playable</c>.</summary>
    Playable,

    /// <summary>Starts and gets in-game, but has serious issues, written <c>runs</c>.</summary>
    Runs,

    /// <summary>Starts but does not reach gameplay, written <c>boots</c>.</summary>
    Boots,

    /// <summary>Does not start, written <c>broken</c>.</summary>
    Broken,
}
